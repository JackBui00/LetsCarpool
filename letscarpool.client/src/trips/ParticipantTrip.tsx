import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { demoPlace, enabledGoogleMapsKey, type PlaceSelection } from '../places/placeSelection';
import { LocationPicker } from '../places/LocationPicker';
import { TripMap } from '../places/TripMap';
import { clearCapability, getCapability } from './capabilitySession';
import { tripApi } from './tripApi';
import { readRevision, readTripSummary, selectedLocation, type TripSummary } from './tripTypes';
import { useGuardedRequest } from './useGuardedRequest';
import { Notice, SectionTitle } from './TripUi';

const mapsKey = enabledGoogleMapsKey(import.meta.env.VITE_GOOGLE_MAPS_ENABLED, import.meta.env.VITE_GOOGLE_MAPS_API_KEY);
type Props = { tripId: string; navigate: (path: string) => void };
type Profile = { name: string; origin: PlaceSelection; canDrive: boolean; passengerSeats: number; canMeet: boolean; maxDetourMinutes: number; locationDisclosureAccepted: boolean };
type OwnTravelLeg = { participantId: string; from: { label: string; kind: 'origin' }; to: { label: string; kind: 'rendezvous' }; distanceMeters: number; durationMinutes: number; modeAssumption: 'unspecified' };
type OwnAssignmentGroup = { driver?: { name?: string }; riders?: Array<{ name?: string }>; stops?: Array<{ kind?: string; label?: string }>; independentTravelLegs?: OwnTravelLeg[]; totalDistanceMeters?: number; durationMinutes?: number };
type OwnAssignment = { strategy?: string; group?: OwnAssignmentGroup; independentTravelLegs?: OwnTravelLeg[]; driver?: { name?: string }; riders?: Array<{ name?: string }>; stops?: Array<{ kind?: string; label?: string }>; totalDistanceMeters?: number; durationMinutes?: number };
type SelfView = { revision: number; trip: TripSummary; profile: Profile; assignment?: OwnAssignment | null };

export function ParticipantTrip({ tripId, navigate }: Props) {
    const [token, setToken] = useState(() => getCapability('participant', tripId));
    const [saved, setSaved] = useState<SelfView | null>(null);
    const [draft, setDraft] = useState<Profile | null>(null);
    const [dirty, setDirty] = useState(false);
    const [message, setMessage] = useState('');
    const request = useGuardedRequest();
    const { run, setError } = request;

    const loadSelf = useCallback(async () => {
        if (!token) return;
        await run(() => tripApi<unknown>(`/${encodeURIComponent(tripId)}/participants/me`, token), value => {
            const view = normalizeSelf(value, tripId);
            if (!view) { setError('Your personal trip response did not include your trip entry.'); return; }
            setSaved(view);
            setDraft(view.profile);
            setDirty(false);
        });
    }, [token, tripId, run, setError]);
    useEffect(() => { void loadSelf(); }, [loadSelf]);

    function edit(change: (current: Profile) => Profile) {
        request.invalidate();
        setDirty(true);
        setDraft(current => current ? change(current) : current);
    }

    function save(event: FormEvent) {
        event.preventDefault();
        if (!saved || !draft || !selectedLocation(draft.origin)) return;
        const body = {
            expectedRevision: saved.revision,
            name: draft.name.trim(), origin: selectedLocation(draft.origin),
            canDrive: draft.canDrive, passengerSeats: draft.canDrive ? draft.passengerSeats : 0,
            canMeet: draft.canMeet, maxDetourMinutes: draft.canDrive ? draft.maxDetourMinutes : 30,
            locationDisclosureAccepted: draft.locationDisclosureAccepted,
        };
        void request.run(() => tripApi<unknown>(`/${encodeURIComponent(tripId)}/participants/me`, token, { method: 'PUT', body: JSON.stringify(body) }), () => {
            setMessage('Your trip entry was updated. The previous plan and assignment were cleared.');
            void loadSelf();
        });
    }

    function leaveThisTab() {
        clearCapability('participant', tripId);
        setToken(null);
        navigate('/');
    }

    if (!token) return <section className="trip-panel"><SectionTitle number="01" title="Participant access unavailable" note="This private link does not contain your personal capability." /><Notice kind="error">Opening the trip invitation will not reveal your entry. Use the participant link provided after a successful join.</Notice><button className="secondary-button" onClick={() => navigate('/')}>Go to trip start</button></section>;
    if (!saved || !draft) return <section className="trip-panel"><SectionTitle number="01" title="Your trip entry" note="Loading your private participant view…" />{request.error && <><Notice kind="error">{request.error}</Notice><button className="secondary-button" onClick={leaveThisTab}>Clear this participant access</button></>}</section>;

    const assignment = saved.assignment;
    const group = assignment?.group;
    const stops = group?.stops ?? assignment?.stops ?? [];
    const independentTravelLegs = group?.independentTravelLegs ?? assignment?.independentTravelLegs ?? [];
    const driver = group?.driver ?? assignment?.driver;
    const riders = group?.riders ?? assignment?.riders ?? [];
    const mapPlaces = [
        { id: 'destination', label: 'Shared destination', role: 'Destination' as const, place: demoPlace(saved.trip.destination.label, saved.trip.destination.latitude, saved.trip.destination.longitude) },
        { id: 'origin', label: 'Your starting place', role: 'Participant origin' as const, place: draft.origin },
    ];

    return <>
        <section className="trip-intro compact-intro"><p className="eyebrow">YOUR PRIVATE TRIP ENTRY</p><h1>{saved.trip.name}<br /><em>your ride details.</em></h1><p>Arrive {formatArrival(saved.trip.arrivalAt, saved.trip.timeZoneId)} · {saved.trip.destination.label}.</p><button className="secondary-button" onClick={leaveThisTab}>Clear this tab's participant access</button></section>
        <div className="trip-layout"><div className="trip-form-column">
            <form className="trip-panel" onSubmit={save}><SectionTitle number="01" title="Your details and preferences" note={`Saved trip revision ${saved.revision}. This capability only edits your entry.`} />
                <label>Your name<input required disabled={request.loading} maxLength={80} value={draft.name} onChange={e => edit(current => ({ ...current, name: e.target.value }))} /></label>
                <LocationPicker label="Your starting address or venue" value={draft.origin} apiKey={mapsKey} disabled={request.loading} onEdit={place => edit(current => ({ ...current, origin: place }))} onResolved={place => edit(current => ({ ...current, origin: place }))} />
                <div className="preference-box"><label className="inline-check"><input type="checkbox" disabled={request.loading} checked={draft.canDrive} onChange={e => edit(current => ({ ...current, canDrive: e.target.checked, passengerSeats: e.target.checked ? current.passengerSeats : 0 }))} /> I can drive a car</label>
                    {draft.canDrive && <><label>Passenger seats available (driver not included)<input disabled={request.loading} type="number" min="0" max="8" value={draft.passengerSeats} onChange={e => edit(current => ({ ...current, passengerSeats: Math.min(8, Math.max(0, Number(e.target.value))) }))} /></label><label>Maximum extra estimated vehicle time per driver <span className="range-value">{draft.maxDetourMinutes} min</span><input disabled={request.loading} type="range" min="0" max="1440" step="5" value={draft.maxDetourMinutes} onChange={e => edit(current => ({ ...current, maxDetourMinutes: Number(e.target.value) }))} /></label></>}
                    <label className="inline-check"><input type="checkbox" disabled={request.loading} checked={draft.canMeet} onChange={e => edit(current => ({ ...current, canMeet: e.target.checked }))} /> I'm okay traveling independently to a meetup</label>
                </div>
                {!draft.locationDisclosureAccepted && <section className="trip-panel disclosure-panel"><h2>Before sharing your location</h2><p>Your starting place is visible to the organizer and the carpool companions assigned to your group.</p><label className="inline-check"><input type="checkbox" disabled={request.loading} checked={draft.locationDisclosureAccepted} onChange={e => edit(current => ({ ...current, locationDisclosureAccepted: e.target.checked }))} /> I understand who can see my location</label></section>}
                {draft.locationDisclosureAccepted && <p className="status-note">Your previously accepted location sharing remains recorded for the organizer and assigned carpool companions.</p>}
                {dirty && <Notice>Your changes are not saved yet. Saving them clears any plan based on your earlier response.</Notice>}
                {request.error && <Notice kind="error">{request.error} <button type="button" className="secondary-button" onClick={() => void loadSelf()}>Reload my entry</button></Notice>}
                {message && <p className="status-note" role="status">{message}</p>}
                <button className="primary-button" type="submit" disabled={request.loading || !dirty || !draft.locationDisclosureAccepted || !selectedLocation(draft.origin)}>{request.loading ? 'Saving…' : 'Save my changes'}</button>
            </form>
            <section className="trip-panel"><SectionTitle number="02" title="Your saved assignment" note="Only your own assigned group and necessary stops appear here." />
                {dirty ? <Notice>Your entry has unsaved edits. The assignment below still reflects your last saved response.</Notice> : null}
                {!assignment ? <p className="status-note">The organizer has not selected a current plan yet.</p> : <OwnAssignmentCard assignment={assignment} driver={driver} riders={riders} stops={stops} independentTravelLegs={independentTravelLegs} totalDistanceMeters={assignment.totalDistanceMeters ?? group?.totalDistanceMeters} durationMinutes={assignment.durationMinutes ?? group?.durationMinutes} />}
                <p className="status-note">Distances are estimates, not road routing. Arrival time is coordination information and is not guaranteed.</p>
            </section>
        </div><TripMap apiKey={mapsKey} locations={mapPlaces} /></div>
    </>;
}

function OwnAssignmentCard({ assignment, driver, riders, stops, independentTravelLegs, totalDistanceMeters, durationMinutes }: { assignment: OwnAssignment; driver?: { name?: string }; riders: Array<{ name?: string }>; stops: Array<{ kind?: string; label?: string }>; independentTravelLegs: OwnTravelLeg[]; totalDistanceMeters?: number; durationMinutes?: number }) {
    return <div className="assignment-card"><h2>{assignment.strategy === 'rendezvous' ? 'Meet along the way' : assignment.strategy === 'pickup' ? 'Pick up along the way' : 'Your carpool assignment'}</h2>
        <p><strong>Driver:</strong> {driver?.name ?? 'See your group details'}</p>
        <p><strong>Assigned companions:</strong> {riders.map(person => person.name).filter(Boolean).join(', ') || 'See your group details'}</p>
        {totalDistanceMeters !== undefined && <p>Estimated group travel {formatDistance(totalDistanceMeters)} · {durationMinutes ?? '—'} estimated minutes.</p>}
        {stops.length > 0 && <ol className="assignment-stops">{stops.map((stop, index) => <li key={`${stop.kind}-${index}`}>{stop.kind === 'destination' ? 'Shared destination' : stop.kind === 'rendezvous' ? 'Meetup point' : 'Pickup'} · {stop.label ?? 'location details'}</li>)}</ol>}
        {independentTravelLegs.map(leg => <p className="assignment-travel-leg" key={leg.participantId}><strong>Your independent travel estimate:</strong> {leg.from.label} to {leg.to.label} · {formatDistance(leg.distanceMeters)} · {leg.durationMinutes} estimated minutes. Travel mode is unspecified; this uses a straight-line estimate.</p>)}
    </div>;
}

function normalizeSelf(value: unknown, tripId: string): SelfView | null {
    if (!value || typeof value !== 'object') return null;
    const source = value as Record<string, unknown>;
    const profile = (source.profile && typeof source.profile === 'object' ? source.profile : source) as Record<string, unknown>;
    const trip = readTripSummary(value);
    const revision = readRevision(value);
    const disclosureAccepted = profile.locationDisclosureAccepted ?? source.locationDisclosureAccepted;
    if (!trip || trip.tripId !== tripId || revision === null || typeof profile.name !== 'string' || typeof profile.canDrive !== 'boolean' || typeof profile.passengerSeats !== 'number' || !Number.isFinite(profile.passengerSeats) || typeof profile.canMeet !== 'boolean' || typeof profile.maxDetourMinutes !== 'number' || !Number.isFinite(profile.maxDetourMinutes) || typeof disclosureAccepted !== 'boolean') return null;
    const rawOrigin = profile.origin as { label?: string; latitude?: number; longitude?: number } | undefined;
    if (typeof rawOrigin?.label !== 'string' || !Number.isFinite(rawOrigin.latitude) || !Number.isFinite(rawOrigin.longitude)) return null;
    const origin: PlaceSelection = { label: rawOrigin.label, latitude: rawOrigin.latitude!, longitude: rawOrigin.longitude!, placeId: null, source: 'google' };
    const assignment = source.selectedAssignment ?? source.assignment;
    return {
        revision, trip,
        profile: { name: profile.name, origin, canDrive: profile.canDrive, passengerSeats: profile.passengerSeats, canMeet: profile.canMeet, maxDetourMinutes: profile.maxDetourMinutes, locationDisclosureAccepted: disclosureAccepted },
        assignment: assignment && typeof assignment === 'object' && !('alternatives' in assignment) ? assignment as OwnAssignment : null,
    };
}

function formatArrival(value: string, zone: string) { try { return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: zone }).format(new Date(value)); } catch { return value; } }
function formatDistance(meters: number) { return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${Math.round(meters)} m`; }
