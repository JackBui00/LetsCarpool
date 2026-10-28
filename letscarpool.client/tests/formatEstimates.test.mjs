import test from 'node:test';
import assert from 'node:assert/strict';
import { formatEstimatedMinutes } from '../src/trips/formatEstimates.ts';

test('estimated minutes use one decimal place without changing source values', () => {
    assert.equal(formatEstimatedMinutes(6.0346418396677315), '6.0');
    assert.equal(formatEstimatedMinutes(2.25), '2.3');
    assert.equal(formatEstimatedMinutes(0), '0.0');
});
