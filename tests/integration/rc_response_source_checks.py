"""Actual response formatter plus owned RC transport; isolated persistence."""
import types
import unittest
from unittest.mock import AsyncMock, patch

from agent import remote_controller_runtime as runtime
from agent.services import response_parser


class ResponseSourceChecks(unittest.IsolatedAsyncioTestCase):
    async def test_normal_channel_retains_its_rich_message_contract(self):
        layer = types.SimpleNamespace(group_send=AsyncMock())
        with patch.object(response_parser, 'get_or_create_bot_user', AsyncMock(return_value=(object(), False))), \
             patch.object(response_parser, 'save_message', AsyncMock(return_value=73)):
            rendered = await response_parser.process_llm_response('<b>safe</b>\nEND-RESPONSE', None, layer, 'owned', persist_artifacts=False)
        event = layer.group_send.call_args.args[1]
        self.assertEqual(event['message'], rendered)
        self.assertIn('<b>safe</b>', rendered)
        self.assertNotIn('source_response', event)
        self.assertEqual(event['message_id'], 73)

    async def test_real_formatter_delivers_lossless_owned_source_and_system_text(self):
        frames = []
        async def progress(frame):
            frames.append(frame)
        session = runtime.RuntimeSession(types.SimpleNamespace(pk=77001), progress)
        session._capturing = True
        underlying = types.SimpleNamespace(group_send=AsyncMock())
        layer = runtime._OwnedChannelLayer(underlying, 'owned', session)
        with patch.object(response_parser, 'get_or_create_bot_user', AsyncMock(return_value=(object(), False))), \
             patch.object(response_parser, 'save_message', AsyncMock(return_value=74)), \
             patch.object(response_parser, '_render_exec_denied_banner', return_value='<p>Execution interrupted</p>'):
            await response_parser.process_llm_response('<b>safe &amp; literal</b>\nEND-RESPONSE', None, layer, 'owned',
                                                      persist_artifacts=False, exec_report_denied={'denied': True})
        self.assertEqual(session._final_message['text'], '<b>safe &amp; literal</b>\nExecution interrupted')
        self.assertEqual(frames[-1]['message_id'], 74)
        self.assertIsNone(session._source_answer)
        self.assertIn('<b>safe &amp; literal</b>', underlying.group_send.call_args.args[1]['message'])
