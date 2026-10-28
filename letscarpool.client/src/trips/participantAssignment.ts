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
    ownIndependentTravelLeg: OwnTravelLeg | null;
};

export function readOwnAssignment(value: unknown, participantId: string): OwnAssignment | null {
    if (!isRecord(value) || (value.strategy !== 'pickup' && value.strategy !== 'rendezvous')
        || (value.role !== 'driver' && value.role !== 'rider') || !isNamedPerson(value.driver)
        || !Array.isArray(value.riders) || !value.riders.every(isNamedPerson)
        || !Array.isArray(value.stops) || !value.stops.every(isPlanStop)
        || !isNonnegative(value.totalDistanceMeters) || !isNonnegative(value.vehicleDistanceMeters)
        || !isNonnegative(value.independentTravelDistanceMeters) || !isNonnegative(value.durationMinutes)) return null;
    const ownLeg = value.ownIndependentTravelLeg === null ? null : readOwnTravelLeg(value.ownIndependentTravelLeg, participantId);
    if (value.ownIndependentTravelLeg !== null && !ownLeg) return null;
    if (value.strategy === 'pickup' && ownLeg !== null) return null;
    return {
        strategy: value.strategy, role: value.role, driver: value.driver,
        riders: value.riders, stops: value.stops, totalDistanceMeters: value.totalDistanceMeters,
        vehicleDistanceMeters: value.vehicleDistanceMeters,
        independentTravelDistanceMeters: value.independentTravelDistanceMeters,
        durationMinutes: value.durationMinutes, ownIndependentTravelLeg: ownLeg,
    };
}

function readOwnTravelLeg(value: unknown, participantId: string): OwnTravelLeg | null {
    if (!isRecord(value) || value.participantId !== participantId || !isNonnegative(value.distanceMeters)
        || !isNonnegative(value.durationMinutes) || value.modeAssumption !== 'unspecified'
        || !isRecord(value.from) || value.from.participantId !== participantId || typeof value.from.label !== 'string'
        || !isCoordinate(value.from.latitude, 90) || !isCoordinate(value.from.longitude, 180)
        || value.from.kind !== 'origin' || !isRecord(value.to) || typeof value.to.participantId !== 'string'
        || typeof value.to.label !== 'string' || !isCoordinate(value.to.latitude, 90) || !isCoordinate(value.to.longitude, 180)
        || value.to.kind !== 'rendezvous') return null;
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
