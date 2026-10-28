import assert from 'node:assert/strict';
import test from 'node:test';
import { isoWithZoneOffset, isValidLocalTimeInZone, localInputInZone } from '../src/trips/dateTime.ts';

test('trip arrival includes the offset for the explicitly selected IANA timezone', () => {
    const iso = isoWithZoneOffset('2026-10-27T18:30', 'America/Los_Angeles');
    assert.equal(iso, '2026-10-27T18:30:00-07:00');
    assert.equal(localInputInZone(iso, 'America/Los_Angeles'), '2026-10-27T18:30');
});

test('nonexistent local arrival time during a daylight-saving jump is rejected', () => {
    assert.equal(isValidLocalTimeInZone('2026-03-08T02:30', 'America/Los_Angeles'), false);
    assert.throws(() => isoWithZoneOffset('2026-03-08T02:30', 'America/Los_Angeles'), /does not exist/);
});
