// Offline authorization and routing tests for the default-branch owner comment workflow.
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;

const lines = fs.readFileSync('.github/workflows/owner-command.yml', 'utf8').split(/\r?\n/);
const block = lines.findIndex(line => line.trim() === 'script: |');
assert.ok(block >= 0, 'GitHub-script owner router is missing');
const script = [];
for (const line of lines.slice(block + 1)) {
  if (line && !line.startsWith('            ')) break;
  script.push(line.startsWith('            ') ? line.slice(12) : '');
}
const route = new AsyncFunction('github', 'context', 'core', script.join('\n'));
const SHA = 'a'.repeat(40);
const OTHER = 'b'.repeat(40);
const REQUIRED = [
  'Build ARSAS', 'Validate IO List Testing',
  'Validate SV evidence bundles', 'Validate COMTRADE viewer integration',
  'CI P0 Workflow Integrity', 'Validate ARSAS Windows installer',
  'IEC 61850 Interoperability Reference Guard'
];

function harness(body, opts = {}) {
  const dispatched = [], comments = [], apiCalls = [];
  const pr = {
    state: opts.merged ? 'closed' : 'open',
    merged_at: opts.merged ? '2026-11-21T00:00:00Z' : null,
    merge_commit_sha: SHA,
    head: { sha: SHA, ref: 'feature/test', repo: { full_name: opts.fork ? 'outsider/fork' : 'masarray/arsas' } }
  };
  const github = {
    rest: {
      pulls: { get: async () => { apiCalls.push('pulls.get'); return { data: pr }; } },
      repos: {
        getBranch: async ({ branch }) => ({
          data: { commit: { sha: branch === 'main' ? (opts.mainSha || SHA) : (opts.branchSha || SHA) } }
        }),
        getContent: async ({ path }) => {
          const content = path === 'VERSION' ? (opts.version || '1.6.43') :
            JSON.stringify({ version: opts.version || '1.6.43', channel: 'stable' });
          return { data: { encoding: 'base64', content: Buffer.from(content).toString('base64') } };
        },
        getLatestRelease: async () => ({ data: { tag_name: 'v1.6.42' } })
      },
      issues: {
        get: async ({ issue_number }) => ({
          data: { state: opts.openIssue === issue_number ? 'open' : 'closed', state_reason: 'completed' }
        }),
        createComment: async args => { comments.push(args.body); }
      },
      actions: {
        listWorkflowRunsForRepo: async () => REQUIRED
          .filter(name => name !== opts.missingCI)
          .map(name => ({ name, conclusion: 'success' })),
        createWorkflowDispatch: async args => { dispatched.push(args); }
      }
    },
    paginate: async (fn, args) => fn(args)
  };
  const context = {
    repo: { owner: 'masarray', repo: 'arsas' },
    issue: { number: 770 },
    payload: { comment: { body, user: { login: opts.actor || 'masarray' },
      author_association: opts.association || 'OWNER' } }
  };
  const core = { info: () => {} };
  return { github, context, core, dispatched, comments, apiCalls };
}

test('rejects non-owner and role-spoofed comments without GitHub writes', async () => {
  for (const options of [{ actor: 'attacker' }, { association: 'CONTRIBUTOR' }]) {
    const h = harness('/arsas release publish', options);
    await route(h.github, h.context, h.core);
    assert.deepEqual(h.dispatched, []);
    assert.deepEqual(h.comments, []);
    assert.deepEqual(h.apiCalls, []);
  }
});

test('FAST reporting routes exact same-repo SHA and bounded focus', async () => {
  const h = harness('/arsas ci fast reporting');
  await route(h.github, h.context, h.core);
  assert.equal(h.dispatched.length, 1);
  assert.equal(h.dispatched[0].workflow_id, 'ci-p1-shadow.yml');
  assert.equal(h.dispatched[0].ref, 'feature/test');
  assert.equal(h.dispatched[0].inputs.expected_source_sha, SHA);
  assert.equal(h.dispatched[0].inputs.focus, 'reporting');
  assert.equal(h.dispatched[0].inputs.fast_dev, 'true');
  assert.equal(h.comments.length, 1);
});

test('unexpected/fork/changed branch cannot dispatch CI', async () => {
  const malformed = harness('/arsas ci fast reporting --unsafe');
  await route(malformed.github, malformed.context, malformed.core);
  assert.deepEqual(malformed.dispatched, []);
  assert.equal(malformed.comments.length, 1);
  for (const options of [{ fork: true }, { branchSha: OTHER }]) {
    const h = harness('/arsas ci full', options);
    await assert.rejects(route(h.github, h.context, h.core));
    assert.deepEqual(h.dispatched, []);
  }
});

test('public stable refuses open PR, old version, moved main, missing P0 or CI', async () => {
  for (const options of [
    {}, { merged: true, version: '1.6.42' },
    { merged: true, mainSha: OTHER },
    { merged: true, openIssue: 769 },
    { merged: true, openIssue: 172 },
    { merged: true, missingCI: 'Validate SV evidence bundles' }
  ]) {
    const h = harness('/arsas release publish', options);
    await assert.rejects(route(h.github, h.context, h.core));
    assert.deepEqual(h.dispatched, []);
  }
});

test('qualified merged-main publication dispatches only exact approved source', async () => {
  const h = harness('/arsas release publish', { merged: true });
  await route(h.github, h.context, h.core);
  assert.equal(h.dispatched.length, 1);
  const payload = h.dispatched[0];
  assert.equal(payload.ref, 'main');
  assert.equal(payload.workflow_id, 'release-windows.yml');
  assert.equal(payload.inputs.expected_source_sha, SHA);
  assert.equal(payload.inputs.publish_release, 'true');
  assert.equal(payload.inputs.prerelease, 'false');
  assert.equal(payload.inputs.version, '1.6.43');
});

test('verify-only mode never requests publication', async () => {
  const h = harness('/arsas release verify');
  await route(h.github, h.context, h.core);
  assert.equal(h.dispatched.length, 1);
  assert.equal(h.dispatched[0].inputs.publish_release, 'false');
  assert.equal(h.dispatched[0].inputs.prerelease, 'true');
});
