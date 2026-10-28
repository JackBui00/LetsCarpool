import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { demoPlace, enabledGoogleMapsKey, type PlaceSelection } from '../places/placeSelection';
import { LocationPicker } from '../places/LocationPicker';
import { TripMap } from '../places/TripMap';
import { clearCapability, getCapability, setCapability } from './capabilitySession';
import { tripApi } from './tripApi';
import { readGeneratedPlan, readRevision, readTripSummary, selectedLocation, type GeneratedPlanDto, type TripSummary } from './tripTypes';
import { useGuardedRequest } from './useGuardedRequest';
import { isValidLocalTimeInZone, isValidTimeZone, isoWithZoneOffset, localInputInZone } from './dateTime';
import { Notice, SectionTitle } from './TripUi';

const mapsKey = enabledGoogleMapsKey(import.meta.env.VITE_GOOGLE_MAPS_ENABLED, import.meta.env.VITE_GOOGLE_MAPS_API_KEY);
type Props = { tripId: string; navigate: (path: string) => void };
type Person = { participantId?: string; id?: string; name?: string; origin?: { label?: string; latitude?: number; longitude?: number }; canDrive?: boolean; passengerSeats?: number; canMeet?: boolean; status?: string };
type GeneratedPlan = GeneratedPlanDto;
type Overview = { trip: TripSummary; revision: number; participants: Person[]; currentPlan?: GeneratedPlan; selectedStrategy?: string; inviteCapability?: string; organizerCapabilityExpiresAt?: string };

export function OrganizerTrip({ tripId, navigate }: Props) {
    const [token, setToken] = useState(() => getCapability('organizer', tripId));
    const [overview, setOverview] = useState<Overview | null>(null);
    const [draft, setDraft] = useState<{ name: string; arrivalLocal: string; timeZoneId: string; destination: PlaceSelection } | null>(null);
    const [dirty, setDirty] = useState(false);
    const [plan, setPlan] = useState<GeneratedPlan | null>(null);
    const [selectedStrategy, setSelectedStrategy] = useState('');
    const [inviteToken, setInviteToken] = useState(() => getCapability('invite', tripId));
    const [inviteExpiry, setInviteExpiry] = useState('');
    const [inviteRevoked, setInviteRevoked] = useState(false);
    const [message, setMessage] = useState('');
    const request = useGuardedRequest();
    const { run, setError } = request;

    const loadOverview = useCallback(async () => {
        if (!token) return;
        await run(() => tripApi<unknown>(`/${encodeURIComponent(tripId)}/organizer`, token), raw => {
            const parsed = normalizeOverview(raw, tripId);
            if (!parsed) { setError('The organizer response was missing required trip fields.'); return; }
            setOverview(parsed);
            setDraft({ name: parsed.trip.name, arrivalLocal: localInputInZone(parsed.trip.arrivalAt, parsed.trip.timeZoneId), timeZoneId: parsed.trip.timeZoneId, destination: dtoPlace(parsed.trip.destination) });
            setPlan(parsed.currentPlan ?? null);
            setSelectedStrategy(parsed.selectedStrategy ?? '');
            if (parsed.inviteCapability) setInviteToken(parsed.inviteCapability);
            if (parsed.trip.inviteExpiresAt) setInviteExpiry(parsed.trip.inviteExpiresAt);
            setDirty(false);
        });
    }, [token, tripId, run, setError]);

    useEffect(() => { void loadOverview(); }, [loadOverview]);

    function edit(update: (current: NonNullable<typeof draft>) => NonNullable<typeof draft>) {
        request.invalidate();
        setDirty(true);
        setPlan(null);
        setDraft(current => current ? update(current) : current);
    }

    function saveTrip(event: FormEvent) {
        event.preventDefault();
        if (!overview || !draft || !selectedLocation(draft.destination)) return;
        const fields: Record<string, unknown> = {};
        if (draft.name !== overview.trip.name) fields.name = draft.name.trim();
        if (draft.arrivalLocal !== localInputInZone(overview.trip.arrivalAt, overview.trip.timeZoneId) || draft.timeZoneId !== overview.trip.timeZoneId) fields.arrivalAt = isoWithZoneOffset(draft.arrivalLocal, draft.timeZoneId);
        if (draft.timeZoneId !== overview.trip.timeZoneId) fields.timeZoneId = draft.timeZoneId;
        const original = overview.trip.destination;
        if (draft.destination.label !== original.label || draft.destination.latitude !== original.latitude || draft.destination.longitude !== original.longitude) fields.destination = selectedLocation(draft.destination);
        if (Object.keys(fields).length === 0) { setDirty(false); return; }
        void request.run(() => tripApi<unknown>(`/${encodeURIComponent(tripId)}/organizer`, token, { method: 'PATCH', body: JSON.stringify({ expectedRevision: overview.revision, ...fields }) }), () => { setMessage('Trip details saved. Any previous plan was cleared.'); void loadOverview(); });
    }

    function generatePlan() {
        if (!overview || dirty) return;
        void request.run(async () => {
            const value = await tripApi<unknown>(`/${encodeURIComponent(tripId)}/plans`, token, { method: 'POST', body: JSON.stringify({ expectedRevision: overview.revision }) });
            const parsed = readGeneratedPlan(value);
            if (!parsed || parsed.basedOnRevision !== overview.revision) throw new Error('The trip service returned an incomplete or stale plan response.');
            return parsed;
        }, value => {
            setPlan(value);
            setSelectedStrategy('');
            setMessage(`Draft plan generated from trip revision ${value.basedOnRevision}.`);
        });
    }

    function selectPlan(strategy: string) {
        if (!overview || !plan || dirty || plan.basedOnRevision !== overview.revision) return;
        void request.run(() => tripApi<unknown>(`/${encodeURIComponent(tripId)}/plans/${encodeURIComponent(plan.planId)}/select`, token, { method: 'POST', body: JSON.stringify({ expectedRevision: overview.revision, strategy }) }), () => {
            setSelectedStrategy(strategy);
            setMessage(`${formatStrategy(strategy)} selected for the current revision.`);
            void loadOverview();
        });
    }

    async function copyCapability(kind: 'invite' | 'organizer') {
        const capability = kind === 'invite' ? inviteToken : token;
        if (!capability) { setMessage(kind === 'invite' ? 'The invitation has been revoked. Rotate it before sharing.' : 'No organizer capability is available in this tab.'); return; }
        const link = kind === 'invite'
            ? `${window.location.origin}/join/${encodeURIComponent(tripId)}#invite=${encodeURIComponent(capability)}`
            : `${window.location.origin}/trips/${encodeURIComponent(tripId)}#organizer=${encodeURIComponent(capability)}`;
        try { await navigator.clipboard.writeText(link); setMessage(kind === 'invite' ? 'Guest invitation copied. Share it privately.' : 'Organizer management link copied. Keep it private and separate from the guest invite.'); }
        catch { setMessage('Clipboard access was unavailable. This link was not copied.'); }
    }

    function rotateInvite(revoke: boolean) {
        const action = revoke ? 'revoke' : 'rotate';
        void request.run(async () => {
            const result = await tripApi<unknown>(`/${encodeURIComponent(tripId)}/invite/${action}`, token, { method: 'POST' });
            if (!revoke && !isRotatedInvite(result)) throw new Error('The service did not return the new guest invitation capability and expiry.');
            return result as { inviteCapability?: string; inviteExpiresAt?: string } | undefined;
        }, result => {
            if (revoke) {
                clearCapability('invite', tripId);
                setInviteToken(null);
                setInviteRevoked(true);
                setMessage('The previous guest invite is revoked.');
            } else if (result?.inviteCapability && result.inviteExpiresAt) {
                setCapability('invite', tripId, result.inviteCapability);
                setInviteToken(result.inviteCapability);
                setInviteRevoked(false);
                if (result.inviteExpiresAt) setInviteExpiry(result.inviteExpiresAt);
                setMessage('A new guest invite was created. The previous link no longer works.');
            }
        });
    }

    function rotateOrganizer() {
        if (!window.confirm('Rotate the organizer capability? This will invalidate the management link in other tabs.')) return;
        void request.run(async () => {
            const result = await tripApi<unknown>(`/${encodeURIComponent(tripId)}/organizer/rotate`, token, { method: 'POST' });
            if (!isRotatedOrganizer(result)) throw new Error('The service did not return the new organizer capability.');
            return result;
        }, result => {
            setCapability('organizer', tripId, result.organizerCapability);
            setToken(result.organizerCapability);
            setMessage('Organizer capability rotated. The previous management link no longer works.');
        });
    }

    if (!token) return <section className="trip-panel"><SectionTitle number="01" title="Organizer access unavailable" note="This route does not contain an organizer capability." /><Notice kind="error">Open the separate organizer management link. A guest invite or trip ID does not grant organizer access.</Notice><button className="secondary-button" onClick={() => navigate('/')}>Create a trip</button></section>;
    if (!overview || !draft) return <section className="trip-panel"><SectionTitle number="01" title="Organizer workspace" note="Loading private trip details…" />{request.error && <Notice kind="error">{request.error}</Notice>}</section>;

    const activePlan = plan ?? overview.currentPlan ?? null;
    const planStale = Boolean(activePlan && (dirty || activePlan.basedOnRevision !== overview.revision));
    const mapLocations = [
        { id: 'destination', label: 'Shared destination', role: 'Destination' as const, place: draft.destination },
        ...overview.participants.map((person, index) => ({ id: person.participantId ?? person.id ?? `participant-${index}`, label: person.name || `Participant ${index + 1}`, role: 'Participant origin' as const, place: person.origin?.latitude != null && person.origin.longitude != null ? dtoPlace(person.origin) : { ...demoPlace('', 0, 0), latitude: null, longitude: null, source: 'unresolved' as const } })),
    ];

    return <>
        <section className="trip-intro compact-intro"><p className="eyebrow">PRIVATE ORGANIZER WORKSPACE</p><div className="organizer-heading"><h1>{overview.trip.name}</h1><div className="organizer-actions"><button className="secondary-button" onClick={rotateInvite.bind(null, false)} disabled={request.loading}>Rotate guest invite</button><button className="danger-button" onClick={rotateInvite.bind(null, true)} disabled={request.loading || inviteRevoked}>Revoke guest invite</button></div></div><p>Trip management access is separate from the guest invitation. Only share the guest link.</p></section>
        <div className="trip-layout"><div className="trip-form-column">
            <section className="trip-panel"><SectionTitle number="01" title="Share the right link" note="These capabilities grant different permissions." />
                <div className="capability-card"><strong>Guest invitation · participant access only</strong><span>Guests can join and edit only their own entry. Invitation expires {inviteExpiry ? formatArrival(inviteExpiry, overview.trip.timeZoneId) : 'after 7 days'}.</span><button className="copy-button" onClick={() => void copyCapability('invite')} disabled={!inviteToken}>Copy guest invite link</button></div>
                <div className="capability-card"><strong>Organizer management · keep private</strong><span>Controls trip details, joined participant roster, plan generation and selection.</span><div className="copy-actions"><button className="secondary-button" onClick={() => void copyCapability('organizer')}>Copy organizer link</button><button className="danger-button" onClick={rotateOrganizer} disabled={request.loading}>Rotate organizer access</button></div></div>
                <p className="status-note">This preview creates local links on {window.location.origin}; friends on other devices cannot open them until the app is hosted.</p>
                {message && <p className="status-note" role="status">{message}</p>}
            </section>
            <form className="trip-panel" onSubmit={saveTrip}><SectionTitle number="02" title="Trip details" note={`Saved revision ${overview.revision}. Arrival time is coordination information, not a routing guarantee.`} />
                <label>Trip name<input required disabled={request.loading} maxLength={100} value={draft.name} onChange={e => edit(current => ({ ...current, name: e.target.value }))} /></label>
                <label>Arrival date and time in trip timezone<input required disabled={request.loading} type="datetime-local" value={draft.arrivalLocal} onChange={e => edit(current => ({ ...current, arrivalLocal: e.target.value }))} /></label>
                <label>Trip timezone (IANA)<input required disabled={request.loading} value={draft.timeZoneId} onChange={e => edit(current => ({ ...current, timeZoneId: e.target.value }))} /><small>Saved timezone: {overview.trip.timeZoneId}.</small></label>
                {!isValidTimeZone(draft.timeZoneId) && <p className="location-error" role="alert">Enter a valid IANA timezone.</p>}
                {isValidTimeZone(draft.timeZoneId) && !isValidLocalTimeInZone(draft.arrivalLocal, draft.timeZoneId) && <p className="location-error" role="alert">Choose a local arrival time that exists in this timezone.</p>}
                <LocationPicker label="Destination address or venue" value={draft.destination} apiKey={mapsKey} disabled={request.loading} onEdit={place => edit(current => ({ ...current, destination: place }))} onResolved={place => edit(current => ({ ...current, destination: place }))} />
                {dirty && <p className="warning">Unsaved edits invalidate the visible draft plan. Save them before generating a replacement.</p>}
                {request.error && <Notice kind="error">{request.error} <button type="button" className="secondary-button" onClick={() => void loadOverview()}>Reload current trip</button></Notice>}
                <button className="primary-button" type="submit" disabled={request.loading || !dirty || !selectedLocation(draft.destination) || !isValidTimeZone(draft.timeZoneId) || !isValidLocalTimeInZone(draft.arrivalLocal, draft.timeZoneId)}>{request.loading ? 'Saving…' : 'Save trip details'}</button>
            </form>
            <section className="trip-panel"><SectionTitle number="03" title={`Joined people · ${overview.participants.length} of 6`} note="Creating a trip does not use a seat; you can join through the same guest flow." />
                {overview.participants.length === 0 ? <p className="status-note">No one has joined yet. Copy the guest invitation to share it privately.</p> : <ul className="roster-list">{overview.participants.map((person, index) => <li key={person.participantId ?? person.id ?? index}><strong>{person.name || 'Participant'}</strong><span>{person.canDrive ? `Offers ${person.passengerSeats ?? 0} passenger seats` : 'Rider'} · {person.canMeet ? 'Can travel to a meetup' : 'Pickup at origin only'}</span>{person.origin?.label && <span>{person.origin.label}</span>}</li>)}</ul>}
            </section>
            <section className="trip-panel"><SectionTitle number="04" title="Compare and select a plan" note="Each generation is bound to this saved trip revision." />
                <p className="status-note">Estimates use straight-line distance and assumed travel speed. No road route, traffic estimate, or arrival deadline is enforced.</p>
                <button className="primary-button" onClick={generatePlan} disabled={request.loading || dirty}>{request.loading ? 'Working…' : 'Generate current trip plan'}</button>
                {planStale && <Notice kind="error">This draft belongs to an earlier trip revision. Generate a new plan after saving changes.</Notice>}
                {activePlan && !planStale && <PlanOptions plan={activePlan} selectedStrategy={selectedStrategy} loading={request.loading} onSelect={selectPlan} />}
            </section>
            {request.error && <Notice kind="error">{request.error}</Notice>}
        </div><TripMap apiKey={mapsKey} locations={mapLocations} /></div>
    </>;
}

function PlanOptions({ plan, selectedStrategy, loading, onSelect }: { plan: GeneratedPlan; selectedStrategy: string; loading: boolean; onSelect: (strategy: string) => void }) {
    const alternatives = plan.plan.alternatives;
    return <div className="plan-card"><h3>{selectedStrategy ? `Selected: ${formatStrategy(selectedStrategy)}` : 'Draft plan · not selected'}</h3><p>Based on revision {plan.basedOnRevision}. Exact among supported pickup and rendezvous strategies within the search limit.</p>
        {alternatives.length === 0 ? <p>No feasible assignment is available with the current responses and seats.</p> : alternatives.map(option => <article className="plan-card" key={option.strategy}><h3>{formatStrategy(option.strategy)} · {formatDistance(option.totalDistanceMeters ?? 0)} · {option.durationMinutes ?? 0} estimated min</h3>
            {option.groups?.map((group, index) => <p key={index}><strong>{group.driver?.name ?? 'Driver'} drives</strong> · {(group.riders ?? []).map(rider => rider.name).filter(Boolean).join(', ') || 'driver only'}<br />{(group.stops ?? []).map(stop => `${stop.kind === 'destination' ? 'Destination' : stop.kind === 'rendezvous' ? 'Meetup' : 'Pickup'}: ${stop.label ?? 'stop'}`).join(' → ')}</p>)}
            {(option.unassigned ?? []).map(person => <p key={person.participantId}>Unassigned {person.participantId}: {person.reason}</p>)}
            {(option.warnings ?? []).map((warning, index) => <p key={index}>{warning}</p>)}
            <button className="secondary-button" onClick={() => onSelect(option.strategy)} disabled={loading}>{selectedStrategy === option.strategy ? 'Selected' : `Select ${formatStrategy(option.strategy)}`}</button>
        </article>)}
    </div>;
}

function normalizeOverview(value: unknown, tripId: string): Overview | null {
    if (!value || typeof value !== 'object') return null;
    const source = value as Record<string, unknown>;
    const trip = readTripSummary(value);
    const revision = readRevision(value);
    if (!trip || revision === null || trip.tripId !== tripId) return null;
    const rawParticipants = source.participants;
    if (!Array.isArray(rawParticipants)) return null;
    const participants = rawParticipants as Person[];
    const rawPlan = source.currentPlan ?? source.draftPlan ?? source.selectedPlan;
    const currentPlan = rawPlan && typeof rawPlan === 'object' ? readGeneratedPlan(rawPlan) ?? undefined : undefined;
    return {
        trip: { ...trip, tripId: trip.tripId || tripId }, revision, participants,
        currentPlan, selectedStrategy: typeof source.selectedStrategy === 'string' ? source.selectedStrategy : undefined,
        inviteCapability: typeof source.inviteCapability === 'string' ? source.inviteCapability : undefined,
        organizerCapabilityExpiresAt: typeof source.organizerCapabilityExpiresAt === 'string' ? source.organizerCapabilityExpiresAt : undefined,
    };
}

function isRotatedInvite(value: unknown): value is { inviteCapability: string; inviteExpiresAt: string } {
    if (!value || typeof value !== 'object') return false;
    const item = value as Record<string, unknown>;
    return typeof item.inviteCapability === 'string' && Boolean(item.inviteCapability) && typeof item.inviteExpiresAt === 'string';
}
function isRotatedOrganizer(value: unknown): value is { organizerCapability: string } {
    if (!value || typeof value !== 'object') return false;
    const item = value as Record<string, unknown>;
    return typeof item.organizerCapability === 'string' && Boolean(item.organizerCapability);
}

function dtoPlace(location: { label?: string; latitude?: number; longitude?: number }): PlaceSelection {
    return { label: location.label ?? '', latitude: location.latitude ?? null, longitude: location.longitude ?? null, placeId: null, source: location.latitude != null && location.longitude != null ? 'google' : 'unresolved' };
}
function formatArrival(value: string, zone: string) { try { return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: zone }).format(new Date(value)); } catch { return value; } }
function formatStrategy(strategy: string) { return strategy === 'rendezvous' ? 'Meet at a shared point' : strategy === 'pickup' ? 'Pick up along the way' : strategy; }
function formatDistance(meters: number) { return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${Math.round(meters)} m`; }
