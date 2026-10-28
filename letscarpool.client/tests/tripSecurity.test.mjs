import assert from 'node:assert/strict';
import test from 'node:test';
import { capabilityStorageKey, getCapability, readCapabilityFragment } from '../src/trips/capabilitySession.ts';
import { tripApi } from '../src/trips/tripApi.ts';
import { readGeneratedPlan, selectedLocation } from '../src/trips/tripTypes.ts';
import { demoPlace } from '../src/places/placeSelection.ts';

function memoryStorage() {
    const values = new Map();
    return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
}

test('invite and organizer fragments are removed and stored under separate session keys', () => {
    const storage = memoryStorage();
    const replaced = [];
    const invite = readCapabilityFragment('/join/trip-a', '', '#invite=invite-secret', storage, url => replaced.push(url));
    const owner = readCapabilityFragment('/manage/trip-a', '?tab=summary', '#owner=owner-secret', storage, url => replaced.push(url));
    assert.deepEqual(invite, { role: 'invite', tripId: 'trip-a' });
    assert.deepEqual(owner, { role: 'organizer', tripId: 'trip-a' });
    assert.deepEqual(replaced, ['/join/trip-a', '/manage/trip-a?tab=summary']);
    assert.equal(getCapability('invite', 'trip-a', storage), 'invite-secret');
    assert.equal(getCapability('organizer', 'trip-a', storage), 'owner-secret');
    assert.notEqual(capabilityStorageKey('invite', 'trip-a'), capabilityStorageKey('organizer', 'trip-a'));
});

test('canonical organizer links use /trips and #organizer, and strip the fragment', () => {
    const storage = memoryStorage();
    const replaced = [];
    const result = readCapabilityFragment('/trips/trip-b', '', '#organizer=owner-secret', storage, url => replaced.push(url));
    assert.deepEqual(result, { role: 'organizer', tripId: 'trip-b' });
    assert.equal(getCapability('organizer', 'trip-b', storage), 'owner-secret');
    assert.deepEqual(replaced, ['/trips/trip-b']);
});

test('trip plan response wraps the existing planner DTO and rejects an unwrapped response', () => {
    const alternative = {
        strategy: 'pickup', totalDistanceMeters: 1200, durationMinutes: 1.8,
        distanceSource: 'haversine-estimate', optimality: 'exact-within-limit', unassigned: [], warnings: [],
        groups: [{
            driver: { id: 'd1', name: 'Driver' }, riders: [{ id: 'r1', name: 'Rider' }],
            stops: [{ participantId: 'r1', label: 'Home', latitude: 37.7, longitude: -122.4, kind: 'pickup' }, { participantId: null, label: 'Venue', latitude: 37.8, longitude: -122.3, kind: 'destination' }],
            independentTravelLegs: [], vehicleDistanceMeters: 1200, independentTravelDistanceMeters: 0,
            totalDistanceMeters: 1200, durationMinutes: 1.8, driverDetourMinutes: 0,
        }],
    };
    const generated = { planId: 'plan-1', basedOnRevision: 2, plan: { alternatives: [alternative], searchOperations: 12, searchOperationLimit: 100000 } };
    assert.deepEqual(readGeneratedPlan(generated), generated);
    assert.equal(readGeneratedPlan({ planId: 'plan-1', basedOnRevision: 2, alternatives: [alternative] }), null);
});

test('role API calls use bearer header and keep capability out of request URL', async () => {
    let called;
    await tripApi('/trip-a/participants', 'private-token', {
        method: 'POST', headers: { 'Idempotency-Key': 'attempt-1' }, body: JSON.stringify({ name: 'Alex' }),
    }, async (url, init) => {
        called = { url, init };
        return new Response(JSON.stringify({ participantId: 'p1' }), { status: 201, headers: { 'Content-Type': 'application/json' } });
    });
    assert.equal(called.url, '/api/v1/trips/trip-a/participants');
    assert.equal(called.init.headers.get('Authorization'), 'Bearer private-token');
    assert.equal(called.init.headers.get('Idempotency-Key'), 'attempt-1');
    assert.equal(called.url.includes('private-token'), false);
});

test('participant capability is distinct from guest invitation access', () => {
    const storage = memoryStorage();
    storage.setItem(capabilityStorageKey('participant', 'trip-a'), 'participant-secret');
    assert.equal(getCapability('invite', 'trip-a', storage), null);
    assert.equal(getCapability('participant', 'trip-a', storage), 'participant-secret');
});

test('persisted trip locations must come from a selected Places result, not demo coordinates', () => {
    assert.equal(selectedLocation(demoPlace('Sample', 37.7, -122.4)), null);
});

test('join replay conflicts expose a safe code and do not return a new capability', async () => {
    await assert.rejects(tripApi('/trip-a/participants', 'invite', { method: 'POST' }, async () => new Response(JSON.stringify({ type: 'urn:trip:join_already_processed', detail: 'Processed; capability cannot be recovered.' }), { status: 409 })), error => {
        assert.equal(error.problemCode, 'join_already_processed');
        assert.match(error.message, /cannot be recovered/);
        return true;
    });
});
