import type { PlaceSelection } from '../places/placeSelection.ts';
import { selectedTripLocation } from './tripTypes.ts';

export type ParticipantProfileDraft = {
    name: string;
    origin: PlaceSelection;
    canDrive: boolean;
    passengerSeats: number;
    canMeet: boolean;
    maxDetourMinutes: number;
};

export function participantProfileUpdate(expectedRevision: number, draft: ParticipantProfileDraft) {
    const origin = selectedTripLocation(draft.origin);
    if (!origin) return null;
    return {
        expectedRevision,
        name: draft.name.trim(),
        origin,
        canDrive: draft.canDrive,
        passengerSeats: draft.canDrive ? draft.passengerSeats : 0,
        canMeet: draft.canMeet,
        maxDetourMinutes: draft.canDrive ? draft.maxDetourMinutes : 30,
    };
}
