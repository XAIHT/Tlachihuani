# Tlachihuani — Created by Angela López Mendoza · XAIHT
"""Real isolated ORM/migration checks loaded only by rc_main_checks.py.

The parent harness supplies a brand-new SQLite file under this checkout's
artifacts. No production AppConfig, historical catalog migration, native service
helper, model or application startup is executed.
"""
import asyncio
import importlib
import importlib.util
from pathlib import Path
import sys
import unittest
import uuid
from unittest.mock import patch

from django.apps import apps
from django.contrib.auth.models import User
from django.db import connection, transaction
from django.db.migrations.autodetector import MigrationAutodetector
from django.db.migrations.graph import MigrationGraph
from django.db.migrations.state import ProjectState

from agent import remote_controller_bridge as bridge
from agent.models import AgentMessage, RemoteControllerProfile, RemoteControllerRevocation


class RemoteControllerSchemaChecks(unittest.TestCase):
    """Exercise actual SQLite persistence; service IPC is explicitly isolated."""
    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        cls.database = Path(connection.settings_dict["NAME"]).resolve()
        artifacts = Path(__file__).resolve().parents[2] / "artifacts" / "rc-implementation"
        if (not cls.database.is_relative_to(artifacts.resolve())
                or not cls.database.parent.name.startswith("main-schema-")
                or cls.database.name != "accounts.sqlite3"):
            raise RuntimeError("Schema acceptance requires the fresh owned database from rc_main_checks.py")
        with connection.cursor() as cursor:
            if connection.introspection.table_names(cursor):
                raise RuntimeError("Schema acceptance refuses an existing populated schema")

        # User's normal Django deletion collector sees every registered related
        # model. Give it empty tables rather than altering or mocking deletion.
        excluded = {RemoteControllerProfile, RemoteControllerRevocation}
        models = [model for model in apps.get_models()
                  if model not in excluded and model._meta.managed and not model._meta.proxy]
        with connection.schema_editor() as editor:
            for model in models:
                editor.create_model(model)

        # Exercise the additive artifact-manifest migration from the actual
        # pre-225 AgentMessage table; no production database is involved.
        with connection.schema_editor() as editor:
            editor.remove_field(AgentMessage, AgentMessage._meta.get_field("response_files"))
        state = ProjectState.from_apps(apps)
        state.remove_field("agent", "agentmessage", "response_files")
        state.remove_model("agent", "remotecontrollerprofile")
        state.remove_model("agent", "remotecontrollerrevocation")
        module = importlib.import_module("agent.migrations.0224_remote_controller")
        migration = module.Migration("0224_remote_controller", "agent")
        with connection.schema_editor() as editor:
            cls.applied_state = migration.apply(state, editor)
        module = importlib.import_module("agent.migrations.0225_response_files")
        migration = module.Migration("0225_response_files", "agent")
        with connection.schema_editor() as editor:
            cls.applied_state = migration.apply(cls.applied_state, editor)

    def setUp(self):
        self.assertEqual(Path(connection.settings_dict["NAME"]).resolve(), self.database)
        # These accounts exist only in the freshly created, checked fixture DB.
        User.objects.filter(username__startswith="rc-acceptance-").delete()
        RemoteControllerRevocation.objects.all().delete()

    @staticmethod
    def account(*, enabled=True, bound=True, username=None):
        user = User.objects.create(username=username or "rc-acceptance-" + uuid.uuid4().hex,
                                   is_active=True)
        profile = RemoteControllerProfile.objects.create(
            user=user, enabled=enabled, binding_id=uuid.uuid4().hex if bound else "",
        )
        return user, profile

    @staticmethod
    def remember(profile):
        return RemoteControllerRevocation.objects.get_or_create(
            binding_id=profile.binding_id,
            defaults={"account": profile.account, "adapter": profile.adapter},
        )[0]

    def fresh_bridge(self):
        name = "agent._rc_restarted_bridge_" + uuid.uuid4().hex
        spec = importlib.util.spec_from_file_location(name, bridge.__file__)
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        self.addCleanup(sys.modules.pop, name, None)
        spec.loader.exec_module(module)
        self.assertFalse(module._started)
        return module

    def revoke_with(self, module, tombstone, *, error=None):
        with patch.object(module, "native", return_value={"disabled": True}, side_effect=error) as native:
            async def request():
                return await module._revoke(tombstone, module._NativeTransport())
            accepted = asyncio.run(request())
        return accepted, native

    def test_migration_state_matches_both_current_models(self):
        changes = MigrationAutodetector(
            self.applied_state, ProjectState.from_apps(apps),
        ).changes(graph=MigrationGraph())
        self.assertEqual(changes, {}, "Migrations 0224 and 0225 must match the live models")

    def test_migration_creates_real_profile_columns(self):
        with connection.cursor() as cursor:
            columns = connection.introspection.get_table_description(cursor, RemoteControllerProfile._meta.db_table)
        self.assertEqual({column.name for column in columns},
                         {field.column for field in RemoteControllerProfile._meta.local_fields})
        _, profile = self.account()
        stored = RemoteControllerProfile.objects.get(pk=profile.pk)
        self.assertEqual((stored.account, stored.adapter), (profile.account, profile.adapter))

    def test_migration_creates_tombstone_columns_without_user_foreign_key(self):
        with connection.cursor() as cursor:
            columns = connection.introspection.get_table_description(cursor, RemoteControllerRevocation._meta.db_table)
            constraints = connection.introspection.get_constraints(cursor, RemoteControllerRevocation._meta.db_table)
        self.assertEqual({column.name for column in columns},
                         {field.column for field in RemoteControllerRevocation._meta.local_fields})
        self.assertFalse(any(value.get("foreign_key") for value in constraints.values()))

    def test_deleting_enabled_bound_user_retains_exact_revocation_identity(self):
        user, profile = self.account()
        user_pk = user.pk
        identity = (profile.binding_id, profile.account, profile.adapter)
        with patch.object(bridge, "native") as native:
            user.delete()
        self.assertFalse(User.objects.filter(pk=user_pk).exists())
        self.assertFalse(RemoteControllerProfile.objects.filter(pk=user_pk).exists())
        tombstone = RemoteControllerRevocation.objects.get(binding_id=identity[0])
        self.assertEqual((tombstone.binding_id, tombstone.account, tombstone.adapter), identity)
        native.assert_not_called()

    def test_deleting_disabled_but_still_bound_user_keeps_pending_revocation(self):
        user, profile = self.account(enabled=False)
        user.delete()
        self.assertTrue(RemoteControllerRevocation.objects.filter(binding_id=profile.binding_id).exists())

    def test_deleting_never_bound_user_creates_no_tombstone(self):
        user, _ = self.account(enabled=False, bound=False)
        user.delete()
        self.assertFalse(RemoteControllerRevocation.objects.exists())

    def test_deleting_profile_alone_preserves_user_and_records_revocation(self):
        user, profile = self.account()
        binding_id = profile.binding_id
        profile.delete()
        self.assertTrue(User.objects.filter(pk=user.pk).exists())
        self.assertTrue(RemoteControllerRevocation.objects.filter(binding_id=binding_id).exists())

    def test_rolled_back_user_delete_also_rolls_back_revocation_intent(self):
        user, profile = self.account()
        user_pk, binding_id = user.pk, profile.binding_id
        with self.assertRaisesRegex(RuntimeError, "fixture rollback"):
            with transaction.atomic():
                user.delete()
                self.assertTrue(RemoteControllerRevocation.objects.filter(binding_id=binding_id).exists())
                raise RuntimeError("fixture rollback")
        self.assertTrue(User.objects.filter(pk=user_pk).exists())
        self.assertTrue(RemoteControllerProfile.objects.filter(pk=user_pk).exists())
        self.assertFalse(RemoteControllerRevocation.objects.filter(binding_id=binding_id).exists())

    def test_duplicate_worker_and_delete_intents_remain_one_durable_row(self):
        user, profile = self.account()
        bridge._remember_revocation(profile)
        bridge._remember_revocation(profile)
        user.delete()
        self.assertEqual(RemoteControllerRevocation.objects.filter(binding_id=profile.binding_id).count(), 1)

    def test_revocation_survives_database_connection_recreation(self):
        user, profile = self.account()
        user.delete()
        previous_connection = connection.connection
        connection.close()
        connection.ensure_connection()
        self.assertIsNot(connection.connection, previous_connection)
        self.assertTrue(self.database.is_file())
        self.assertEqual(RemoteControllerRevocation.objects.get(binding_id=profile.binding_id).adapter,
                         profile.adapter)

    def test_fresh_bridge_instance_discovers_revocation_without_a_user(self):
        user, profile = self.account()
        user.delete()
        connection.close()
        restarted = self.fresh_bridge()
        pending = restarted._pending_revocations()
        self.assertEqual([(item.binding_id, item.account, item.adapter) for item in pending],
                         [(profile.binding_id, profile.account, profile.adapter)])
        self.assertFalse(RemoteControllerProfile.objects.exists())

    def test_service_outage_keeps_row_for_fresh_bridge_to_retry(self):
        user, profile = self.account()
        user.delete()
        tombstone = RemoteControllerRevocation.objects.get(binding_id=profile.binding_id)
        restarted = self.fresh_bridge()
        accepted, _ = self.revoke_with(
            restarted, tombstone, error=restarted.RcBridgeError("service_unavailable", "fixture"),
        )
        self.assertFalse(accepted)
        connection.close()
        self.assertEqual(len(self.fresh_bridge()._pending_revocations()), 1)

    def test_successful_acknowledgement_removes_only_matching_tombstone(self):
        user, profile = self.account()
        user.delete()
        unrelated = RemoteControllerRevocation.objects.create(
            binding_id=uuid.uuid4().hex, account=uuid.uuid4(), adapter=uuid.uuid4(),
        )
        restarted = self.fresh_bridge()
        accepted, native = self.revoke_with(
            restarted, RemoteControllerRevocation.objects.get(binding_id=profile.binding_id),
        )
        self.assertTrue(accepted)
        self.assertFalse(RemoteControllerRevocation.objects.filter(binding_id=profile.binding_id).exists())
        self.assertTrue(RemoteControllerRevocation.objects.filter(binding_id=unrelated.binding_id).exists())
        native.assert_called_once_with({"op": "unbind-account", "binding_id": profile.binding_id,
                                        "adapter_id": profile.adapter.hex})

    def test_recreated_username_has_new_identity_and_keeps_new_access(self):
        username = "rc-acceptance-recreated"
        user, old = self.account(username=username)
        user.delete()
        replacement_user, replacement = self.account(username=username)
        self.assertNotEqual(old.account, replacement.account)
        self.assertNotEqual(old.adapter, replacement.adapter)
        self.assertNotEqual(old.binding_id, replacement.binding_id)
        restarted = self.fresh_bridge()
        accepted, native = self.revoke_with(
            restarted, RemoteControllerRevocation.objects.get(binding_id=old.binding_id),
        )
        self.assertTrue(accepted)
        replacement.refresh_from_db()
        self.assertTrue(replacement.enabled)
        self.assertTrue(User.objects.filter(pk=replacement_user.pk).exists())
        self.assertEqual(native.call_args.args[0]["binding_id"], old.binding_id)

    def test_new_explicit_consent_supersedes_old_pending_intent(self):
        _, profile = self.account(enabled=False)
        tombstone = self.remember(profile)
        profile.enabled = True
        profile.save(update_fields=["enabled"])
        restarted = self.fresh_bridge()
        accepted, native = self.revoke_with(restarted, tombstone)
        self.assertTrue(accepted)
        native.assert_not_called()
        profile.refresh_from_db()
        self.assertTrue(profile.enabled)
        self.assertFalse(RemoteControllerRevocation.objects.filter(binding_id=profile.binding_id).exists())

    def test_disabled_account_retry_clears_binding_but_preserves_user_identity(self):
        user, profile = self.account(enabled=False)
        account_id, adapter_id, binding_id = profile.account, profile.adapter, profile.binding_id
        tombstone = self.remember(profile)
        restarted = self.fresh_bridge()
        accepted, _ = self.revoke_with(restarted, tombstone)
        self.assertTrue(accepted)
        profile.refresh_from_db()
        self.assertFalse(profile.enabled)
        self.assertEqual(profile.binding_id, "")
        self.assertEqual((profile.account, profile.adapter), (account_id, adapter_id))
        self.assertTrue(User.objects.filter(pk=user.pk).exists())
        self.assertFalse(RemoteControllerRevocation.objects.filter(binding_id=binding_id).exists())
