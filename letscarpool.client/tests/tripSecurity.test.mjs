import assert from 'node:assert/strict';
import test from 'node:test';
import { capabilityStorageKey, getCapability, getOrganizerExpiry, participantContinuation, readCapabilityFragment, setOrganizerExpiry } from '../src/trips/capabilitySession.ts';
import { tripApi } from '../src/trips/tripApi.ts';
import { participantProfileUpdate } from '../src/trips/participantProfile.ts';
import { isPlanStale, readAssignmentRole, readCreatedTrip, readGeneratedPlan, readLocationDto, readTripSummary, selectedLocation, selectedTripLocation } from '../src/trips/tripTypes.ts';
import { readOwnAssignment } from '../src/trips/participantAssignment.ts';
import { classifyJoinFailure, hasTripMayHaveJoined, joinAttemptStorageKey, legacyJoinAttemptKey, markTripMayHaveJoined, readJoinAttempt, removeJoinAttempt, writeJoinAttempt } from '../src/trips/joinAttemptSession.ts';
import { demoPlace, editPlaceLabel } from '../src/places/placeSelection.ts';

function memoryStorage() {
    const values = new Map();
    return { values, getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
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

test('rendezvous plan accepts only the server-provided independent travel estimate', () => {
    const leg = {
        participantId: 'r1',
        from: { participantId: 'r1', label: 'Rider origin', latitude: 37.71, longitude: -122.42, kind: 'origin' },
        to: { participantId: 'd1', label: 'Meetup', latitude: 37.7, longitude: -122.4, kind: 'rendezvous' },
        distanceMeters: 1750, durationMinutes: 2.6, modeAssumption: 'unspecified',
    };
    const group = {
        driver: { id: 'd1', name: 'Driver' }, riders: [{ id: 'r1', name: 'Rider' }],
        stops: [{ participantId: 'd1', label: 'Meetup', latitude: 37.7, longitude: -122.4, kind: 'rendezvous' }, { participantId: null, label: 'Venue', latitude: 37.8, longitude: -122.3, kind: 'destination' }],
        independentTravelLegs: [leg], vehicleDistanceMeters: 1200, independentTravelDistanceMeters: 1750,
        totalDistanceMeters: 2950, durationMinutes: 4.4, driverDetourMinutes: 0,
    };
    const alternative = {
        strategy: 'rendezvous', totalDistanceMeters: 2950, durationMinutes: 4.4,
        distanceSource: 'haversine-estimate', optimality: 'exact-within-limit', unassigned: [], warnings: [], groups: [group],
    };
    const response = { planId: 'plan-2', basedOnRevision: 3, plan: { alternatives: [alternative], searchOperations: 25, searchOperationLimit: 100000 } };
    assert.deepEqual(readGeneratedPlan(response), response);
    assert.equal(readGeneratedPlan({ ...response, plan: { ...response.plan, alternatives: [{ ...alternative, groups: [{ ...group, independentTravelLegs: [{ ...leg, modeAssumption: 'walking' }] }] }] } }), null);
});

test('backend participant assignment DTO renders only the caller independent leg', () => {
    const dto = {
        strategy: 'rendezvous', role: 'rider', driver: { id: 'driver-1', name: 'Driver' }, riders: [{ id: 'rider-1', name: 'Rider' }],
        stops: [{ participantId: 'driver-1', label: 'Meetup', latitude: 37.7, longitude: -122.4, kind: 'rendezvous' }, { participantId: null, label: 'Venue', latitude: 37.8, longitude: -122.3, kind: 'destination' }],
        totalDistanceMeters: 2600, vehicleDistanceMeters: 1800, independentTravelDistanceMeters: 800, durationMinutes: 3.9,
        independentTravelLegs: [{ participantId: 'rider-1', from: { participantId: 'rider-1', label: 'Rider origin', latitude: 37.71, longitude: -122.42, kind: 'origin' }, to: { participantId: 'driver-1', label: 'Meetup', latitude: 37.7, longitude: -122.4, kind: 'rendezvous' }, distanceMeters: 800, durationMinutes: 1.2, modeAssumption: 'unspecified' }],
    };
    const { independentTravelLegs, ...withoutWireLegs } = dto;
    assert.deepEqual(readOwnAssignment(dto, 'rider-1'), { ...withoutWireLegs, ownIndependentTravelLeg: independentTravelLegs[0] });
    assert.equal(readOwnAssignment({ ...dto, independentTravelLegs: [{ ...dto.independentTravelLegs[0], participantId: 'another-rider' }] }, 'rider-1'), null);
    assert.equal(readOwnAssignment({ ...dto, independentTravelLegs: [{ ...dto.independentTravelLegs[0], modeAssumption: 'walking' }] }, 'rider-1'), null);
    assert.equal(readOwnAssignment({ ...dto, independentTravelLegs: [], strategy: 'pickup' }, 'rider-1')?.ownIndependentTravelLeg, null);
    assert.equal(readOwnAssignment({ ...dto, independentTravelLegs: [dto.independentTravelLegs[0], dto.independentTravelLegs[0]] }, 'rider-1'), null);
});

test('server planStale prevents selection even when revision metadata matches', () => {
    const generated = { planId: 'plan-1', basedOnRevision: 4, plan: { alternatives: [], searchOperations: 1, searchOperationLimit: 100000 } };
    assert.equal(isPlanStale(true, false, 4, generated), true);
    assert.equal(isPlanStale(false, false, 5, generated), true);
    assert.equal(isPlanStale(false, true, 4, generated), true);
    assert.equal(isPlanStale(false, false, 4, generated), false);
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

test('saved participant capability produces a continuation path without exposing the token', () => {
    const storage = memoryStorage();
    storage.setItem(capabilityStorageKey('participant', 'trip-a'), 'participant-secret');
    assert.deepEqual(participantContinuation(Boolean(getCapability('participant', 'trip-a', storage)), 'trip-a'), { path: '/trip/trip-a', label: 'Continue to my participant link' });
    assert.equal(participantContinuation(false, 'trip-a'), null);
});

test('create response requires exact organizer expiry and stores only expiry metadata locally', () => {
    const response = {
        tripId: 'trip-a', revision: 1, organizerCapability: 'owner-test-capability', inviteCapability: 'invite-test-capability',
        inviteExpiresAt: '2027-07-19T18:00:00Z', organizerExpiresAt: '2026-10-29T18:00:00Z',
    };
    assert.deepEqual(readCreatedTrip(response), response);
    assert.equal(readCreatedTrip({ ...response, organizerExpiresAt: undefined }), null);
    const storage = memoryStorage();
    setOrganizerExpiry('trip-a', response.organizerExpiresAt, storage);
    assert.equal(getOrganizerExpiry('trip-a', storage), response.organizerExpiresAt);
    assert.equal([...storage.values.values()].includes('owner-test-capability'), false);
});

test('participant assignment role is read from the response field, never inferred', () => {
    const selectedAssignment = {
        strategy: 'rendezvous', role: 'rider',
        group: { driver: { id: 'd1', name: 'Driver' }, riders: [{ id: 'r1', name: 'Rider' }], stops: [], independentTravelLegs: [{ participantId: 'r1', distanceMeters: 800 }] },
    };
    assert.equal(readAssignmentRole(selectedAssignment), 'rider');
    assert.equal(readAssignmentRole({ ...selectedAssignment, role: 'driver' }), 'driver');
    assert.equal(readAssignmentRole({ strategy: 'pickup', group: selectedAssignment.group }), null);
    assert.equal(readAssignmentRole(null), null);
});

test('trip location contract round-trips required provenance and rejects missing or unknown values', () => {
    const destination = { label: 'Lake Merritt', latitude: 37.8, longitude: -122.26, provenance: 'userProvided' };
    const summary = { tripId: 'trip-a', name: 'Lake day', arrivalAt: '2027-07-12T11:00:00-07:00', timeZoneId: 'America/Los_Angeles', destination, inviteExpiresAt: '2027-07-19T18:00:00Z' };
    assert.deepEqual(readLocationDto(destination), destination);
    assert.equal(readTripSummary(summary)?.destination.provenance, 'userProvided');
    assert.equal(readLocationDto({ ...destination, provenance: undefined }), null);
    assert.equal(readLocationDto({ ...destination, provenance: 'mapbox-searchbox' }), null);
    assert.equal(readTripSummary({ ...summary, destination: { ...destination, provenance: 'unknown' } }), null);
});

test('temporary Search Box results cannot be saved to trips but user pins and permanent results preserve provenance', () => {
    const temporary = { label: 'Search result', latitude: 37.7, longitude: -122.4, placeId: 'mbx-1', source: 'mapbox-searchbox-temporary' };
    const pin = { label: 'My meetup point', latitude: 37.7, longitude: -122.4, placeId: null, source: 'user-provided' };
    const permanent = { label: 'Stored address', latitude: 37.7, longitude: -122.4, placeId: null, source: 'mapbox-geocoding-permanent' };
    assert.deepEqual(selectedLocation(temporary), { label: 'Search result', latitude: 37.7, longitude: -122.4 });
    assert.equal(selectedTripLocation(temporary), null);
    assert.equal(selectedTripLocation(demoPlace('Sample', 37.7, -122.4)), null);
    assert.equal(selectedTripLocation(editPlaceLabel('Unresolved')), null);
    assert.deepEqual(selectedTripLocation(pin), { label: 'My meetup point', latitude: 37.7, longitude: -122.4, provenance: 'userProvided' });
    assert.deepEqual(selectedTripLocation(permanent)?.provenance, 'mapboxGeocodingPermanent');
});

test('participant profile edits omit consent and retain only a genuine resolved trip location', () => {
    const body = participantProfileUpdate(4, {
        name: ' Sam ', origin: { label: 'Meet point', latitude: 37.7, longitude: -122.4, placeId: null, source: 'user-provided' },
        canDrive: true, passengerSeats: 2, canMeet: true, maxDetourMinutes: 45,
    });
    assert.deepEqual(body, {
        expectedRevision: 4, name: 'Sam', origin: { label: 'Meet point', latitude: 37.7, longitude: -122.4, provenance: 'userProvided' },
        canDrive: true, passengerSeats: 2, canMeet: true, maxDetourMinutes: 45,
    });
    assert.equal(Object.hasOwn(body, 'locationDisclosureAccepted'), false);
    assert.equal(participantProfileUpdate(4, {
        name: 'Sam', origin: { label: 'Temporary', latitude: 37.7, longitude: -122.4, placeId: 'place', source: 'mapbox-searchbox-temporary' },
        canDrive: false, passengerSeats: 3, canMeet: true, maxDetourMinutes: 30,
    }), null);
});

test('persisted demo coordinates are never accepted as a planner location', () => {
    assert.equal(selectedLocation(demoPlace('Sample', 37.7, -122.4)), null);
});

test('join replay conflicts expose a safe code and do not return a new capability', async () => {
    await assert.rejects(tripApi('/trip-a/participants', 'invite', { method: 'POST' }, async () => new Response(JSON.stringify({ code: 'join_already_processed', title: 'Join already processed', detail: 'This join request already completed and will not create another participant. Continue with the participant link saved in this browser. If the link was not saved, access cannot be recovered without an account.' }), { status: 409 })), error => {
        assert.equal(error.problemCode, 'join_already_processed');
        assert.match(error.message, /will not create another participant/);
        assert.match(error.message, /Continue with the participant link saved in this browser/);
        assert.match(error.message, /cannot be recovered without an account/);
        return true;
    });
});

test('join attempt snapshots reuse the same UUID/body for the same trip and invite after reload', async () => {
    const storage = memoryStorage();
    const key = await joinAttemptStorageKey('trip-a', 'invite-secret-a');
    const attempt = {
        idempotencyKey: '0d111111-1111-4111-8111-111111111111',
        body: { name: 'Sam', origin: { label: 'Selected place', latitude: 37.7, longitude: -122.4, provenance: 'userProvided' }, canDrive: false, passengerSeats: 0, canMeet: true, maxDetourMinutes: 30, locationDisclosureAccepted: true },
        status: 'retryable',
    };
    assert.equal(writeJoinAttempt(storage, key, attempt), true);
    const reloaded = readJoinAttempt(storage, await joinAttemptStorageKey('trip-a', 'invite-secret-a'));
    assert.deepEqual(reloaded, attempt);
    assert.equal(key.includes('invite-secret-a'), false);
});

test('rotated invite and different trip use separate attempt scopes while risk remains trip-scoped', async () => {
    const storage = memoryStorage();
    const original = await joinAttemptStorageKey('trip-a', 'invite-secret-a');
    const rotated = await joinAttemptStorageKey('trip-a', 'invite-secret-b');
    const otherTrip = await joinAttemptStorageKey('trip-b', 'invite-secret-a');
    assert.notEqual(original, rotated);
    assert.notEqual(original, otherTrip);
    assert.notEqual(rotated, otherTrip);
    assert.equal(original.includes('invite-secret-a'), false);
    assert.equal(legacyJoinAttemptKey('trip-a'), 'gather.join-attempt.trip-a');
    markTripMayHaveJoined(storage, 'trip-a');
    assert.equal(hasTripMayHaveJoined(storage, 'trip-a'), true);
    assert.equal(hasTripMayHaveJoined(storage, 'trip-b'), false);
});

test('join_already_processed remains uncertain without creating a replacement UUID', async () => {
    const storage = memoryStorage();
    const key = await joinAttemptStorageKey('trip-a', 'invite-secret-a');
    const attempt = {
        idempotencyKey: '0d111111-1111-4111-8111-111111111111',
        body: { name: 'Sam', origin: { label: 'Selected place', latitude: 37.7, longitude: -122.4, provenance: 'userProvided' }, canDrive: false, passengerSeats: 0, canMeet: true, maxDetourMinutes: 30, locationDisclosureAccepted: true },
        status: 'uncertain', reason: 'replay',
    };
    assert.equal(classifyJoinFailure(409, 'join_already_processed').mayHaveSucceeded, true);
    assert.equal(classifyJoinFailure(409, 'join_already_processed').status, 'uncertain');
    assert.equal(writeJoinAttempt(storage, key, attempt), true);
    assert.deepEqual(readJoinAttempt(storage, key), attempt);
    assert.equal(classifyJoinFailure(409, 'trip_capacity_reached').mayHaveSucceeded, false);
    assert.equal(classifyJoinFailure(409, 'revision_conflict').mayHaveSucceeded, false);
    removeJoinAttempt(storage, key);
});
