import assert from 'node:assert/strict';
import test from 'node:test';
import { demoPlace, editPlaceLabel, hasMapboxToken, isPlaceResolved, isPlanReady, MapboxProviderError, MapboxSearchSequence, MapboxSearchSession, mapboxProviderMessage, mapboxRetrieveUrl, mapboxSuggestUrl, parseMapboxRetrievedLocation, parseMapboxSuggestions, requestMapboxLocation, requestMapboxSuggestions } from '../src/places/placeSelection.ts';

test('typing over a selected place clears coordinates and place ID', () => {
    const edited = editPlaceLabel('Changed address');
    assert.equal(edited.label, 'Changed address');
    assert.equal(edited.latitude, null);
    assert.equal(edited.longitude, null);
    assert.equal(edited.placeId, null);
    assert.equal(isPlaceResolved(edited), false);
});

test('demo locations are resolved for display but cannot make a plan ready', () => {
    const demo = demoPlace('Demo venue', 37.7, -122.4);
    assert.equal(isPlaceResolved(demo), true);
    assert.equal(hasMapboxToken('  '), false);
    assert.equal(isPlanReady(undefined, [demo]), false);
    assert.equal(isPlanReady('test-public-token', [editPlaceLabel('unselected address')]), false);
    assert.equal(isPlanReady('test-public-token', [demo]), false);
});

test('Search Box URLs encode input, use one session token, and cap suggestions at five', () => {
    const suggest = new URL(mapboxSuggestUrl('test-public-token', 'Lake & Park', 'session-uuid'));
    assert.equal(suggest.pathname, '/search/searchbox/v1/suggest');
    assert.equal(suggest.searchParams.get('q'), 'Lake & Park');
    assert.equal(suggest.searchParams.get('session_token'), 'session-uuid');
    assert.equal(suggest.searchParams.get('limit'), '5');
    const retrieve = new URL(mapboxRetrieveUrl('id/with space', 'test-public-token', 'session-uuid'));
    assert.equal(retrieve.pathname, '/search/searchbox/v1/retrieve/id%2Fwith%20space');
    assert.equal(retrieve.searchParams.get('session_token'), 'session-uuid');
    assert.equal(suggest.searchParams.get('access_token'), 'test-public-token');
});

test('Mapbox Search Box parsing uses [longitude, latitude] and limits results', () => {
    const suggestions = Array.from({ length: 7 }, (_, index) => ({ mapbox_id: `id-${index}`, name: `Place ${index}`, place_formatted: 'San Francisco' }));
    assert.equal(parseMapboxSuggestions({ suggestions }).length, 5);
    const resolved = parseMapboxRetrievedLocation({ features: [{ properties: { full_address: 'Venue, SF', mapbox_id: 'venue-id' }, geometry: { coordinates: [-122.4, 37.7] } }] });
    assert.deepEqual(resolved, { label: 'Venue, SF', longitude: -122.4, latitude: 37.7, mapboxId: 'venue-id' });
    assert.equal(parseMapboxRetrievedLocation({ features: [{ properties: { name: 'bad' }, geometry: { coordinates: [37.7, -122.4] } }] }), null);
});

test('provider errors are generic and never include request URLs or token values', () => {
    for (const status of [401, 403, 429, 500]) {
        const message = mapboxProviderMessage(status);
        assert.equal(message.includes('test-public-token'), false);
        assert.equal(message.includes('https://'), false);
    }
    assert.match(mapboxProviderMessage(401), /not authorized/);
    assert.match(mapboxProviderMessage(429), /usage limit/);
});

test('a delayed Mapbox response is discarded after its search revision is invalidated', async () => {
    const sequence = new MapboxSearchSequence();
    const oldRequest = sequence.begin();
    let resolveOld;
    const pending = new Promise(resolve => { resolveOld = resolve; });
    sequence.invalidate();
    resolveOld([{ mapbox_id: 'stale', name: 'Old result', place_formatted: 'Old result' }]);
    const staleResults = await pending;
    assert.equal(sequence.isCurrent(oldRequest), false);
    assert.equal(staleResults[0].mapbox_id, 'stale');
});

test('Mapbox autocomplete keystrokes share a session through retrieve, then start a new one', () => {
    let next = 0;
    const session = new MapboxSearchSession(() => `session-${++next}`);
    assert.equal(session.current(), 'session-1');
    assert.equal(session.current(), 'session-1');
    session.complete();
    assert.equal(session.current(), 'session-2');
});

test('provider request helpers parse bounded results and return sanitized status errors', async () => {
    const suggestionResponse = new Response(JSON.stringify({ suggestions: Array.from({ length: 8 }, (_, index) => ({ mapbox_id: `p-${index}`, name: `Place ${index}`, place_formatted: 'City' })) }), { status: 200 });
    const results = await requestMapboxSuggestions('test-public-token', 'venue', 'session-id', new AbortController().signal, async () => suggestionResponse);
    assert.equal(results.length, 5);
    const locationResponse = new Response(JSON.stringify({ features: [{ properties: { name: 'Test', mapbox_id: 'p-1' }, geometry: { coordinates: [-122.4, 37.7] } }] }), { status: 200 });
    assert.equal((await requestMapboxLocation('p-1', 'test-public-token', 'session-id', async () => locationResponse))?.longitude, -122.4);
    await assert.rejects(requestMapboxSuggestions('test-public-token', 'venue', 'session-id', new AbortController().signal, async () => new Response('{}', { status: 429 })), error => error instanceof MapboxProviderError && error.status === 429);
});
