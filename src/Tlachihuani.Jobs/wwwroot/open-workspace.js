'use strict';
(async () => {
  const hint = document.getElementById('hint');
  // XT's "Open workspace" adds ?signin=1. The browser then goes to that
  // Tlamatini's own login page (the same /?next=<workspace> address Django uses
  // when nobody is signed in). That page always shows the form, and signing in
  // replaces whatever session this browser held, so the queue that opens next
  // belongs only to the account that just signed in, even when another tab or
  // window of this browser is signed in with a different account.
  const signin = new URLSearchParams(location.search).has('signin');
  const destination = app => {
    if (!signin) return app.url;
    const workspace = new URL(app.url);
    return workspace.origin + '/?next=' + encodeURIComponent(workspace.pathname);
  };
  try {
    const response = await fetch('/controllers', {cache:'no-store'});
    if (!response.ok) throw new Error();
    const apps = await response.json();
    if (apps.length === 1) { location.replace(destination(apps[0])); return; }
    hint.textContent = apps.length ? (signin ? 'Choose the Tlamatini application, then sign in to open your queue.' :
      'Choose the Tlamatini session whose queue you want to open.') :
      'Open Tlamatini and choose Panels → Torrenter Workspace. Sign in to Tlamatini if prompted.';
    for (const app of apps) {
      const link = document.createElement('a'); link.href=destination(app); link.textContent=app.name;
      document.getElementById('apps').append(link);
    }
  } catch { hint.textContent='Open Torrenter Workspace from the Panels menu in Tlamatini.'; }
})();
