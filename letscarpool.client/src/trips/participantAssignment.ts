export type OwnTravelLeg = {
    participantId: string;
    from: { participantId: string; label: string; kind: 'origin' };
    to: { participantId: string; label: string; kind: 'rendezvous' };
    distanceMeters: number;
    durationMinutes: number;
    modeAssumption: 'unspecified';
};

export type OwnAssignment = {
    strategy: 'pickup' | 'rendezvous';
    role: 'driver' | 'rider';
    driver: { id: string; name: string };
    riders: Array<{ id: string; name: string }>;
    stops: Array<{ participantId: string | null; label: string; latitude: number; longitude: number; kind: 'pickup' | 'rendezvous' | 'destination' }>;
    totalDistanceMeters: number;
    vehicleDistanceMeters: number;
    independentTravelDistanceMeters: number;
    durationMinutes: number;
    independentTravelLegs: OwnTravelLeg[];
};

export type ParticipantAssignmentState = 'stale' | 'assigned' | 'unassigned' | 'no-plan';

export function participantAssignmentState(planStale: boolean, assignment: OwnAssignment | null, unassignedReason: string | null): ParticipantAssignmentState {
    if (planStale) return 'stale';
    if (assignment) return 'assigned';
    if (unassignedReason) return 'unassigned';
    return 'no-plan';
}

export function readOwnAssignment(value: unknown, participantId: string): OwnAssignment | null {
    if (!isRecord(value) || (value.strategy !== 'pickup' && value.strategy !== 'rendezvous')
        || (value.role !== 'driver' && value.role !== 'rider') || !isNamedPerson(value.driver)
        || !Array.isArray(value.riders) || !value.riders.every(isNamedPerson)
        || !Array.isArray(value.stops) || !value.stops.every(isPlanStop)
        || !Array.isArray(value.independentTravelLegs)
        || !isNonnegative(value.totalDistanceMeters) || !isNonnegative(value.vehicleDistanceMeters)
        || !isNonnegative(value.independentTravelDistanceMeters) || !isNonnegative(value.durationMinutes)) return null;
    const legs = value.independentTravelLegs.map(leg => readOwnTravelLeg(leg, participantId));
    if (!legs.every((leg): leg is OwnTravelLeg => leg !== null)) return null;
    const expectedLegCount = value.strategy === 'rendezvous' && value.role === 'rider' ? 1 : 0;
    if (legs.length !== expectedLegCount) return null;
    return {
        strategy: value.strategy, role: value.role, driver: value.driver,
        riders: value.riders, stops: value.stops, totalDistanceMeters: value.totalDistanceMeters,
        vehicleDistanceMeters: value.vehicleDistanceMeters,
        independentTravelDistanceMeters: value.independentTravelDistanceMeters,
        durationMinutes: value.durationMinutes, independentTravelLegs: legs,
    };
}

function readOwnTravelLeg(value: unknown, participantId: string): OwnTravelLeg | null {
    if (!isRecord(value) || value.participantId !== participantId || !isNonnegative(value.distanceMeters)
        || !isNonnegative(value.durationMinutes) || value.modeAssumption !== 'unspecified'
        || !isRecord(value.from) || value.from.participantId !== participantId || typeof value.from.label !== 'string'
        || value.from.kind !== 'origin' || !isRecord(value.to) || typeof value.to.participantId !== 'string'
        || typeof value.to.label !== 'string' || value.to.kind !== 'rendezvous') return null;
    return value as OwnTravelLeg;
}

function isPlanStop(value: unknown): value is OwnAssignment['stops'][number] {
    return isRecord(value) && (typeof value.participantId === 'string' || value.participantId === null)
        && typeof value.label === 'string' && isCoordinate(value.latitude, 90)
        && isCoordinate(value.longitude, 180)
        && (value.kind === 'pickup' || value.kind === 'rendezvous' || value.kind === 'destination')
        && (value.kind === 'destination' ? value.participantId === null : typeof value.participantId === 'string');
}

function isCoordinate(value: unknown, maximumMagnitude: number) {
    return typeof value === 'number' && Number.isFinite(value) && Math.abs(value) <= maximumMagnitude;
}

function isNamedPerson(value: unknown): value is { id: string; name: string } {
    return isRecord(value) && typeof value.id === 'string' && typeof value.name === 'string';
}

function isNonnegative(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
