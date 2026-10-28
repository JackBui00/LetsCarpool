import assert from 'node:assert/strict';
import test from 'node:test';
import { demoPlace, editPlaceLabel, enabledGoogleMapsKey, hasGoogleMapsKey, isPlaceResolved, isPlanReady, loadGoogleMaps } from '../src/places/placeSelection.ts';

test('typing over a selected place clears its coordinates and place id', () => {
    const edited = editPlaceLabel('Changed address');
    assert.equal(edited.label, 'Changed address');
    assert.equal(edited.latitude, null);
    assert.equal(edited.longitude, null);
    assert.equal(edited.placeId, null);
    assert.equal(isPlaceResolved(edited), false);
});

test('demo locations are explicitly resolved and keyless search is not loaded', async () => {
    assert.equal(isPlaceResolved(demoPlace('Demo venue', 37.7, -122.4)), true);
    assert.equal(hasGoogleMapsKey('  '), false);
    assert.equal(isPlanReady(undefined, [demoPlace('Demo venue', 37.7, -122.4)]), false);
    assert.equal(isPlanReady('configured-key', [editPlaceLabel('unselected address')]), false);
    assert.equal(isPlanReady('configured-key', [demoPlace('Demo venue', 37.7, -122.4)]), true);
    await assert.rejects(loadGoogleMaps(undefined), /not configured/);
});

test('Maps API key is ignored unless explicit provider opt-in is enabled', () => {
    assert.equal(enabledGoogleMapsKey('false', 'test-key'), undefined);
    assert.equal(enabledGoogleMapsKey(undefined, 'test-key'), undefined);
    assert.equal(enabledGoogleMapsKey('true', 'test-key'), 'test-key');
});
