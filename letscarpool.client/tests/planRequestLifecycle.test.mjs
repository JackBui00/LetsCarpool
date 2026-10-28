import assert from 'node:assert/strict';
import test from 'node:test';
import { PlanRequestRevision, runPlanRequest } from '../src/planRequestLifecycle.ts';

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((resolvePromise, rejectPromise) => {
        resolve = resolvePromise;
        reject = rejectPromise;
    });
    return { promise, resolve, reject };
}

function createState() {
    return { result: null, error: '', loading: false };
}

function handlersFor(state) {
    return {
        onSuccess: value => { state.result = value; },
        onError: error => { state.error = error.message; },
        onFinally: () => { state.loading = false; },
    };
}

test('an edited plan ignores a delayed success and cannot stop a newer request', async () => {
    const revisions = new PlanRequestRevision();
    const state = createState();
    const oldResponse = deferred();
    const oldRevision = revisions.begin();
    state.loading = true;
    const oldRequest = runPlanRequest(revisions, oldRevision, () => oldResponse.promise, handlersFor(state));

    revisions.invalidate(); // an input edit clears old results and loading state
    state.result = null;
    state.error = '';
    state.loading = false;
    const newResponse = deferred();
    const newRevision = revisions.begin();
    state.loading = true;
    const newRequest = runPlanRequest(revisions, newRevision, () => newResponse.promise, handlersFor(state));

    oldResponse.resolve({ plan: 'stale' });
    assert.equal(await oldRequest, 'stale');
    assert.deepEqual(state, { result: null, error: '', loading: true });

    newResponse.resolve({ plan: 'current' });
    assert.equal(await newRequest, 'success');
    assert.deepEqual(state, { result: { plan: 'current' }, error: '', loading: false });
});

test('an edited plan ignores a delayed error and its finally handler', async () => {
    const revisions = new PlanRequestRevision();
    const state = createState();
    const oldResponse = deferred();
    const oldRevision = revisions.begin();
    state.loading = true;
    const oldRequest = runPlanRequest(revisions, oldRevision, () => oldResponse.promise, handlersFor(state));

    revisions.invalidate(); // an input edit clears any obsolete error and loading state
    state.result = null;
    state.error = '';
    state.loading = false;
    const newRevision = revisions.begin();
    state.loading = true;

    oldResponse.reject(new Error('stale failure'));
    assert.equal(await oldRequest, 'stale');
    assert.deepEqual(state, { result: null, error: '', loading: true });
    assert.equal(revisions.isCurrent(newRevision), true);
});

test('a current successful request stores its plan and clears loading', async () => {
    const revisions = new PlanRequestRevision();
    const state = createState();
    state.loading = true;

    const outcome = await runPlanRequest(revisions, revisions.begin(), async () => ({ alternatives: [] }), handlersFor(state));

    assert.equal(outcome, 'success');
    assert.deepEqual(state, { result: { alternatives: [] }, error: '', loading: false });
});

test('a current failed request reports its error and clears loading', async () => {
    const revisions = new PlanRequestRevision();
    const state = createState();
    state.loading = true;

    const outcome = await runPlanRequest(revisions, revisions.begin(), async () => { throw new Error('Validation failed'); }, handlersFor(state));

    assert.equal(outcome, 'error');
    assert.deepEqual(state, { result: null, error: 'Validation failed', loading: false });
});
