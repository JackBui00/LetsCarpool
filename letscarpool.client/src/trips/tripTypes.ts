import type { PlaceSelection } from '../places/placeSelection';
import { isValidTimeZone } from './dateTime.ts';

export type LocationDto = { label: string; latitude: number; longitude: number };
export type TripSummary = { tripId: string; name: string; arrivalAt: string; timeZoneId: string; destination: LocationDto; inviteExpiresAt: string };
export type CreatedTrip = { tripId: string; revision: number; organizerCapability: string; inviteCapability: string; inviteExpiresAt: string };
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
