import type { PlaceSelection } from '../places/placeSelection';
import { isValidTimeZone } from './dateTime.ts';

export type LocationDto = { label: string; latitude: number; longitude: number };
export type TripSummary = { tripId: string; name: string; arrivalAt: string; timeZoneId: string; destination: LocationDto; inviteExpiresAt: string };
export type CreatedTrip = { tripId: string; revision: number; organizerCapability: string; inviteCapability: string; inviteExpiresAt: string };
export type PlanAlternativeDto = {
    strategy: 'pickup' | 'rendezvous';
    totalDistanceMeters: number;
    durationMinutes: number;
    distanceSource: 'haversine-estimate';
    optimality: 'exact-within-limit';
    unassigned: Array<{ participantId: string; reason: string }>;
    warnings: string[];
    groups: Array<{
        driver: { id: string; name: string };
        riders: Array<{ id: string; name: string }>;
        stops: Array<{ participantId: string | null; label: string; latitude: number; longitude: number; kind: 'pickup' | 'rendezvous' | 'destination' }>;
        independentTravelLegs: Array<{
            participantId: string;
            from: { participantId: string; label: string; latitude: number; longitude: number; kind: 'origin' };
            to: { participantId: string; label: string; latitude: number; longitude: number; kind: 'rendezvous' };
            distanceMeters: number;
            durationMinutes: number;
            modeAssumption: 'unspecified';
        }>;
        vehicleDistanceMeters: number;
        independentTravelDistanceMeters: number;
        driverDetourMinutes: number;
    }>;
};
export type PlannerPlanDto = { alternatives: PlanAlternativeDto[]; searchOperations: number; searchOperationLimit: number };
export type GeneratedPlanDto = { planId: string; basedOnRevision: number; plan: PlannerPlanDto };
export type TripParticipantDraft = {
    name: string;
    origin: PlaceSelection;
    canDrive: boolean;
    passengerSeats: number;
    canMeet: boolean;
    maxDetourMinutes: number;
};

export function selectedLocation(place: PlaceSelection): LocationDto | null {
    if (place.source !== 'google' || !place.label.trim() || place.latitude === null || place.longitude === null || !Number.isFinite(place.latitude) || !Number.isFinite(place.longitude)) return null;
    return { label: place.label, latitude: place.latitude, longitude: place.longitude };
}

export function readTripSummary(value: unknown): TripSummary | null {
    if (!value || typeof value !== 'object') return null;
    const source = value as Record<string, unknown>;
    const trip = (source.trip && typeof source.trip === 'object' ? source.trip : source) as Record<string, unknown>;
    const destination = trip.destination as LocationDto | undefined;
    const inviteExpiresAt = trip.inviteExpiresAt ?? source.inviteExpiresAt;
    if (typeof (trip.tripId ?? source.tripId) !== 'string' || !(trip.tripId ?? source.tripId) || typeof trip.name !== 'string' || !trip.name.trim() || typeof trip.arrivalAt !== 'string' || !/[+-]\d{2}:\d{2}$/.test(trip.arrivalAt) || typeof trip.timeZoneId !== 'string' || !isValidTimeZone(trip.timeZoneId) || typeof inviteExpiresAt !== 'string' || !Number.isFinite(Date.parse(inviteExpiresAt)) || typeof destination?.label !== 'string' || !destination.label.trim() || !Number.isFinite(destination.latitude) || !Number.isFinite(destination.longitude)) return null;
    return {
        tripId: String(trip.tripId ?? source.tripId), name: trip.name,
        arrivalAt: trip.arrivalAt, timeZoneId: trip.timeZoneId,
        destination, inviteExpiresAt,
    };
}

export function readRevision(value: unknown) {
    if (!value || typeof value !== 'object') return null;
    const source = value as Record<string, unknown>;
    const nested = source.trip as Record<string, unknown> | undefined;
    const revision = source.revision ?? nested?.revision;
    return typeof revision === 'number' && Number.isSafeInteger(revision) && revision >= 0 ? revision : null;
}

export function readGeneratedPlan(value: unknown): GeneratedPlanDto | null {
    if (!isRecord(value)) return null;
    const { planId, basedOnRevision, plan } = value;
    if (typeof planId !== 'string' || !planId || !isRevision(basedOnRevision) || !isRecord(plan)
        || !Number.isSafeInteger(plan.searchOperations) || !Number.isSafeInteger(plan.searchOperationLimit)
        || !Array.isArray(plan.alternatives) || !plan.alternatives.every(isPlanAlternative)) return null;
    return value as GeneratedPlanDto;
}

function isPlanAlternative(value: unknown): value is PlanAlternativeDto {
    if (!isRecord(value) || (value.strategy !== 'pickup' && value.strategy !== 'rendezvous') || !isFiniteNumber(value.totalDistanceMeters) || !isFiniteNumber(value.durationMinutes)
        || value.distanceSource !== 'haversine-estimate' || value.optimality !== 'exact-within-limit'
        || !Array.isArray(value.unassigned) || !value.unassigned.every(item => isRecord(item) && typeof item.participantId === 'string' && typeof item.reason === 'string')
        || !Array.isArray(value.warnings) || !value.warnings.every(item => typeof item === 'string') || !Array.isArray(value.groups)) return false;
    return value.groups.every(group => isRecord(group)
        && isNamedPerson(group.driver)
        && Array.isArray(group.riders) && group.riders.every(isNamedPerson)
        && Array.isArray(group.stops) && group.stops.every(stop => isRecord(stop)
            && (typeof stop.participantId === 'string' || stop.participantId === null)
            && typeof stop.label === 'string' && isFiniteNumber(stop.latitude) && isFiniteNumber(stop.longitude)
            && (stop.kind === 'pickup' || stop.kind === 'rendezvous' || stop.kind === 'destination'))
        && Array.isArray(group.independentTravelLegs) && group.independentTravelLegs.every(isIndependentTravelLeg)
        && isFiniteNumber(group.vehicleDistanceMeters) && isFiniteNumber(group.independentTravelDistanceMeters)
        && isFiniteNumber(group.totalDistanceMeters) && isFiniteNumber(group.durationMinutes) && isFiniteNumber(group.driverDetourMinutes));
}

function isIndependentTravelLeg(value: unknown) {
    if (!isRecord(value) || typeof value.participantId !== 'string' || !isFiniteNumber(value.distanceMeters) || !isFiniteNumber(value.durationMinutes) || value.modeAssumption !== 'unspecified') return false;
    return isTravelLocation(value.from, 'origin') && isTravelLocation(value.to, 'rendezvous');
}

function isTravelLocation(value: unknown, kind: 'origin' | 'rendezvous') {
    return isRecord(value) && typeof value.participantId === 'string' && typeof value.label === 'string'
        && isFiniteNumber(value.latitude) && isFiniteNumber(value.longitude) && value.kind === kind;
}

function isNamedPerson(value: unknown): value is { id: string; name: string } {
    return isRecord(value) && typeof value.id === 'string' && typeof value.name === 'string';
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isRevision(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isFiniteNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value);
}
