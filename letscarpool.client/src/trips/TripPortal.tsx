import { useEffect, useState, type FormEvent } from 'react';
import { demoPlace, hasMapboxToken, isPlanReady, unresolvedPlace, type PlaceSelection } from '../places/placeSelection';
import { LocationPicker } from '../places/LocationPicker';
import { TripMap } from '../places/TripMap';
import { clearCapability, getCapability, participantContinuation, readCapabilityFragment, setCapability, setOrganizerExpiry } from './capabilitySession';
import { tripApi, TripApiError } from './tripApi';
import { classifyJoinFailure, clearTripMayHaveJoined, hasTripMayHaveJoined, joinAttemptStorageKey, legacyJoinAttemptKey, markTripMayHaveJoined, readJoinAttempt, removeJoinAttempt, writeJoinAttempt, type JoinAttemptRecord, type JoinRequestBody } from './joinAttemptSession';
import { readCreatedTrip, readTripSummary, selectedTripLocation, type TripSummary } from './tripTypes';
import { useGuardedRequest } from './useGuardedRequest';
import { isValidLocalTimeInZone, isValidTimeZone, isoWithZoneOffset } from './dateTime';
import { OrganizerTrip } from './OrganizerTrip';
import { ParticipantTrip } from './ParticipantTrip';
import { Notice, SectionTitle } from './TripUi';
import '../App.css';
import './TripPortal.css';

const mapToken = import.meta.env.VITE_MAPBOX_ACCESS_TOKEN;
const initialCapability = typeof window === 'undefined' ? null : captureInitialCapability();

type RouteProps = { tripId: string; navigate: (path: string) => void };

export function TripPortal() {
    const [path, setPath] = useState(window.location.pathname);
    useEffect(() => {
        const updatePath = () => setPath(window.location.pathname);
        window.addEventListener('popstate', updatePath);
        return () => window.removeEventListener('popstate', updatePath);
    }, []);
    const navigate = (next: string) => { window.history.pushState(null, '', next); setPath(next); };
    const parts = path.split('/').filter(Boolean);
    const tripId = parts[1] ? decodeURIComponent(parts[1]) : '';
    return <main className="trip-shell">
        <Header onHome={() => navigate('/')} />
        {initialCapability?.storageError && <Notice kind="error">This browser could not safely keep the invitation capability for this tab. Reopen the private link after enabling session storage.</Notice>}
        {parts[0] === 'join' && tripId ? <JoinTrip key={`join-${tripId}`} tripId={tripId} navigate={navigate} />
            : (parts[0] === 'trips' || parts[0] === 'manage') && tripId ? <OrganizerTrip key={`manage-${tripId}`} tripId={tripId} navigate={navigate} />
                : parts[0] === 'trip' && tripId ? <ParticipantTrip key={`trip-${tripId}`} tripId={tripId} navigate={navigate} />
                    : <CreateTrip navigate={navigate} />}
        <footer className="trip-footer"><span>Made for the ride together.</span><span>Location data is shared only for trip coordination.</span></footer>
    </main>;
}

function Header({ onHome }: { onHome: () => void }) {
    return <header className="trip-header"><button className="trip-brand" type="button" onClick={onHome} aria-label="Gather home"><span className="brand-mark" aria-hidden="true">g</span> gather<span className="brand-dot">.</span></button><span className="session-badge"><span /> Private trip workspace</span></header>;
}

function CreateTrip({ navigate }: { navigate: (path: string) => void }) {
    const [name, setName] = useState('');
    const [arrivalLocal, setArrivalLocal] = useState(defaultArrival());
    const [timeZoneId, setTimeZoneId] = useState(Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC');
    const [destination, setDestination] = useState<PlaceSelection>(unresolvedPlace(''));
    const [manualTarget, setManualTarget] = useState<{ label: string; role: 'Destination' | 'Participant origin' } | null>(null);
    const request = useGuardedRequest();
    const timezoneValid = isValidTimeZone(timeZoneId);
    const arrivalValid = isValidLocalTimeInZone(arrivalLocal, timeZoneId);
    const canCreate = isPlanReady(mapToken, [destination]) && selectedTripLocation(destination) !== null && name.trim().length > 0 && Boolean(arrivalLocal) && timezoneValid && arrivalValid;

    function edit(work: () => void) { request.invalidate(); work(); }
    function create(event: FormEvent) {
        event.preventDefault();
        if (!canCreate) return;
        if (!canStoreCapability()) {
            request.setError('This browser cannot keep private trip links for this tab. Enable session storage before creating the trip.');
            return;
        }
        const place = selectedTripLocation(destination);
        if (!place) return;
        void request.run(async () => {
            const value = await tripApi<unknown>('', null, { method: 'POST', body: JSON.stringify({ name: name.trim(), arrivalAt: isoWithZoneOffset(arrivalLocal, timeZoneId), timeZoneId, destination: place }) });
            const created = readCreatedTrip(value);
            if (!created) throw new Error('The trip service returned incomplete private access details.');
            try {
                setCapability('organizer', created.tripId, created.organizerCapability);
                setCapability('invite', created.tripId, created.inviteCapability);
            } catch {
                throw new Error('This browser could not keep the private trip links. Enable session storage and create the trip again.');
            }
            try { setOrganizerExpiry(created.tripId, created.organizerExpiresAt); } catch { /* The expiry is not secret and can be fetched from the organizer response later. */ }
            return created;
        }, created => navigate(`/trips/${encodeURIComponent(created.tripId)}`));
    }

    return <>
        <section className="trip-intro"><p className="eyebrow">A PLAN FOR YOUR PEOPLE</p><h1>Make the trip<br /><em>together.</em></h1><p>Create one private trip, then invite each friend to add their own starting place and ride preferences.</p></section>
        {!hasMapboxToken(mapToken) && <Notice>Map search is unavailable until a restricted local Mapbox token is configured. Demo locations are never submitted as real addresses.</Notice>}
        <form className="trip-layout" onSubmit={create}>
            <div className="trip-form-column">
                <section className="trip-panel"><SectionTitle number="01" title="Start a trip" note="Create a private plan for one destination." />
                    <label>Trip name<input required disabled={request.loading} maxLength={100} value={name} onChange={e => edit(() => setName(e.target.value))} placeholder="Saturday concert" /></label>
                    <label>Arrival date and time in trip timezone<input required disabled={request.loading} type="datetime-local" value={arrivalLocal} onChange={e => edit(() => setArrivalLocal(e.target.value))} /></label>
                    <label>Trip timezone (IANA)<input required disabled={request.loading} value={timeZoneId} onChange={e => edit(() => setTimeZoneId(e.target.value))} placeholder="America/Los_Angeles" /><small>Default: this device's timezone. Verify it matches the event location.</small></label>
                    {!timezoneValid && <p className="location-error" role="alert">Enter a valid IANA timezone such as America/Los_Angeles.</p>}
                    {timezoneValid && arrivalLocal && !arrivalValid && <p className="location-error" role="alert">Choose a local arrival time that exists in this timezone.</p>}
                    <LocationPicker label="Destination address or venue" value={destination} token={mapToken} disabled={request.loading} onEdit={place => edit(() => setDestination(place))} onResolved={place => edit(() => setDestination(place))} onManualPick={label => setManualTarget({ label, role: 'Destination' })} />
                    {destination.source === 'mapbox-searchbox-temporary' && <Notice kind="error">This Mapbox Search Box result is temporary and cannot be stored on a trip. Use the manual map pin with a label you provide; permanent geocoding is not enabled.</Notice>}
                </section>
                <section className="trip-panel privacy-panel"><SectionTitle number="02" title="Private by default" note="Guests join with their own link. They cannot manage the trip or edit anyone else." /><p>Creating a trip does not take a participant seat. Join through the guest link if you want to ride too. The group is limited to six joined people.</p></section>
                {request.error && <Notice kind="error">{request.error}</Notice>}
                <button className="primary-button" type="submit" disabled={request.loading || !canCreate}>{request.loading ? 'Creating private trip…' : 'Create trip'}</button>
            </div>
            <TripMap token={mapToken} selecting={manualTarget} onPinSelect={point => { if (!manualTarget) return; edit(() => setDestination({ label: manualTarget.label, latitude: point.latitude, longitude: point.longitude, placeId: null, source: 'user-provided' })); setManualTarget(null); }} locations={[{ id: 'destination', label: 'Shared destination', role: 'Destination', place: destination }]} />
        </form>
    </>;
}

function JoinTrip({ tripId, navigate }: RouteProps) {
    const inviteToken = getCapability('invite', tripId);
    const existingParticipant = getCapability('participant', tripId);
    const continuation = participantContinuation(Boolean(existingParticipant), tripId);
    const [summary, setSummary] = useState<TripSummary | null>(null);
    const [summaryError, setSummaryError] = useState('');
    const [name, setName] = useState('');
    const [origin, setOrigin] = useState<PlaceSelection>(unresolvedPlace(''));
    const [manualTarget, setManualTarget] = useState<{ label: string; role: 'Destination' | 'Participant origin' } | null>(null);
    const [canDrive, setCanDrive] = useState(false);
    const [passengerSeats, setPassengerSeats] = useState(0);
    const [canMeet, setCanMeet] = useState(false);
    const [maxDetourMinutes, setMaxDetourMinutes] = useState(30);
    const [locationDisclosureAccepted, setLocationDisclosureAccepted] = useState(false);
    const [attemptScopeKey, setAttemptScopeKey] = useState<string | null>(null);
    const [attemptScopeReady, setAttemptScopeReady] = useState(false);
    const [attemptScopeError, setAttemptScopeError] = useState('');
    const [attemptKey, setAttemptKey] = useState<string | null>(null);
    const [attemptUncertain, setAttemptUncertain] = useState(false);
    const [attemptUncertainReason, setAttemptUncertainReason] = useState<'replay' | 'interrupted' | null>(null);
    const [sameAttemptRetryAllowed, setSameAttemptRetryAllowed] = useState(false);
    const [retryReady, setRetryReady] = useState(false);
    const [retryAfterSeconds, setRetryAfterSeconds] = useState(0);
    const [attemptBody, setAttemptBody] = useState<JoinRequestBody | null>(null);
    const [capacityReached, setCapacityReached] = useState(false);
    const [priorAttemptMayHaveSucceeded, setPriorAttemptMayHaveSucceeded] = useState(false);
    const [riskAcknowledged, setRiskAcknowledged] = useState(false);
    const request = useGuardedRequest();

    useEffect(() => {
        let active = true;
        setAttemptScopeReady(false);
        setAttemptScopeKey(null);
        setAttemptScopeError('');
        setAttemptKey(null);
        setAttemptBody(null);
        setAttemptUncertain(false);
        setAttemptUncertainReason(null);
        setSameAttemptRetryAllowed(false);
        setRetryReady(false);
        setPriorAttemptMayHaveSucceeded(false);
        setRiskAcknowledged(false);
        if (!inviteToken || existingParticipant) { setAttemptScopeReady(true); return () => { active = false; }; }
        void joinAttemptStorageKey(tripId, inviteToken).then(scopeKey => {
            if (!active) return;
            const legacyKey = legacyJoinAttemptKey(tripId);
            const legacyAttempt = safeSessionGet(legacyKey);
            if (legacyAttempt) {
                safeSessionRemove(legacyKey);
                markTripMayHaveJoined(sessionStorage, tripId);
            }
            const serialized = safeSessionGet(scopeKey);
            const record = readJoinAttempt(sessionStorage, scopeKey);
            if (serialized && !record) markTripMayHaveJoined(sessionStorage, tripId);
            const mayHaveJoined = hasTripMayHaveJoined(sessionStorage, tripId);
            setAttemptScopeKey(scopeKey);
            setAttemptKey(record?.idempotencyKey ?? null);
            setAttemptBody(record?.body ?? null);
            if (record) {
                setName(record.body.name);
                setOrigin({ ...unresolvedPlace(record.body.origin.label), latitude: record.body.origin.latitude, longitude: record.body.origin.longitude, source: placeSourceFromProvenance(record.body.origin.provenance) });
                setCanDrive(record.body.canDrive);
                setPassengerSeats(record.body.passengerSeats);
                setCanMeet(record.body.canMeet);
                setMaxDetourMinutes(record.body.maxDetourMinutes);
                setLocationDisclosureAccepted(record.body.locationDisclosureAccepted);
            }
            setAttemptUncertain(Boolean(record && (record.status === 'uncertain' || record.status === 'pending')));
            setAttemptUncertainReason(record?.reason ?? (record?.status === 'pending' ? 'interrupted' : null));
            setSameAttemptRetryAllowed(Boolean(record && (record.status === 'retryable' || record.status === 'pending' || (record.status === 'uncertain' && record.reason === 'interrupted'))));
            setRetryReady(record?.status === 'retryable');
            setPriorAttemptMayHaveSucceeded(mayHaveJoined);
            setAttemptScopeReady(true);
            if (record?.status === 'pending') {
                const interrupted: JoinAttemptRecord = { ...record, status: 'uncertain', reason: 'interrupted' };
                writeJoinAttempt(sessionStorage, scopeKey, interrupted);
                markTripMayHaveJoined(sessionStorage, tripId);
                setPriorAttemptMayHaveSucceeded(true);
            }
        }).catch(() => {
            if (!active) return;
            setAttemptScopeError('This browser could not prepare a private retry scope. Enable session storage and Web Crypto before joining.');
            setAttemptScopeReady(true);
        });
        return () => { active = false; };
    }, [tripId, inviteToken, existingParticipant]);

    useEffect(() => {
        if (!inviteToken || existingParticipant) return;
        let active = true;
        const controller = new AbortController();
        void tripApi<unknown>(`/${encodeURIComponent(tripId)}/invite`, inviteToken, { signal: controller.signal })
            .then(value => {
                if (!active) return;
                const parsed = readTripSummary(value);
                if (!parsed || parsed.tripId !== tripId) setSummaryError('The invitation summary did not match this trip. Reopen the complete private invite link.');
                else setSummary(parsed);
            })
            .catch(error => { if (active) setSummaryError(error instanceof Error ? error.message : 'The invitation could not be opened.'); });
        return () => { active = false; controller.abort(); };
    }, [tripId, inviteToken, existingParticipant]);

    useEffect(() => {
        if (retryAfterSeconds <= 0) return;
        const timer = window.setTimeout(() => setRetryAfterSeconds(value => Math.max(0, value - 1)), 1000);
        return () => window.clearTimeout(timer);
    }, [retryAfterSeconds]);

    function edit(work: () => void) { request.invalidate(); work(); }

    function join(event: FormEvent) {
        event.preventDefault();
        if (!attemptScopeReady || !attemptScopeKey || !inviteToken || !summary || !name.trim() || !locationDisclosureAccepted || !selectedTripLocation(origin) || attemptUncertain || capacityReached || attemptKey || (priorAttemptMayHaveSucceeded && !riskAcknowledged)) return;
        const body: JoinRequestBody = {
            name: name.trim(), origin: selectedTripLocation(origin)!, canDrive,
            passengerSeats: canDrive ? passengerSeats : 0, canMeet,
            maxDetourMinutes: canDrive ? maxDetourMinutes : 30,
            locationDisclosureAccepted: true,
        };
        const key = crypto.randomUUID();
        sendJoin(key, body);
    }

    function sendJoin(key: string, body: JoinRequestBody) {
        if (!attemptScopeKey) {
            request.setError('The private invite retry scope is not ready. Reopen this invitation before joining.');
            return;
        }
        const pendingAttempt: JoinAttemptRecord = { idempotencyKey: key, body, status: 'pending' };
        if (!writeJoinAttempt(sessionStorage, attemptScopeKey, pendingAttempt)) {
            request.setError('This browser cannot retain the same join request for safe retries. Enable session storage before joining.');
            return;
        }
        setAttemptKey(key);
        setAttemptBody(body);
        setAttemptUncertain(false);
        setAttemptUncertainReason(null);
        setSameAttemptRetryAllowed(false);
        setRetryReady(false);
        void request.run(async () => {
            const result = await tripApi<unknown>(
            `/${encodeURIComponent(tripId)}/participants`, inviteToken,
            { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify(body) },
            );
            if (!isJoinCreated(result)) throw new Error('The trip service did not return personal participant access.');
            return result;
        }, result => {
            setCapability('participant', tripId, result.participantCapability);
            clearCapability('invite', tripId);
            removeJoinAttempt(sessionStorage, attemptScopeKey);
            clearTripMayHaveJoined(sessionStorage, tripId);
            navigate(`/trip/${encodeURIComponent(tripId)}`);
        }, reason => {
            const status = reason instanceof TripApiError ? reason.status : 0;
            const problemCode = reason instanceof TripApiError ? reason.problemCode : undefined;
            const disposition = classifyJoinFailure(status, problemCode);
            if (disposition.mayHaveSucceeded) {
                markTripMayHaveJoined(sessionStorage, tripId);
                setPriorAttemptMayHaveSucceeded(true);
            }
            if (disposition.status === 'retryable') {
                writeJoinAttempt(sessionStorage, attemptScopeKey, { idempotencyKey: key, body, status: 'retryable' });
                setRetryReady(true);
                setSameAttemptRetryAllowed(true);
                setAttemptUncertain(false);
                setAttemptUncertainReason(null);
                if (reason instanceof TripApiError && reason.status === 429 && reason.retryAfter) {
                    const seconds = Number(reason.retryAfter);
                    setRetryAfterSeconds(Number.isFinite(seconds) ? Math.max(0, seconds) : Math.max(0, Math.ceil((Date.parse(reason.retryAfter) - Date.now()) / 1000)));
                }
            } else if (disposition.status === 'uncertain') {
                const replay = problemCode === 'join_already_processed';
                writeJoinAttempt(sessionStorage, attemptScopeKey, { idempotencyKey: key, body, status: 'uncertain', reason: replay ? 'replay' : 'interrupted' });
                setRetryReady(false);
                setAttemptUncertain(true);
                setAttemptUncertainReason(replay ? 'replay' : 'interrupted');
                setSameAttemptRetryAllowed(!replay);
                setRiskAcknowledged(false);
            } else {
                removeJoinAttempt(sessionStorage, attemptScopeKey);
                setAttemptKey(null);
                setAttemptBody(null);
                setAttemptUncertain(false);
                setAttemptUncertainReason(null);
                setSameAttemptRetryAllowed(false);
                setRetryReady(false);
                if (problemCode === 'trip_capacity_reached') {
                    setCapacityReached(true);
                } else if (problemCode === 'revision_conflict') {
                    setSummaryError('The trip changed. Reopen the invite to load its current details.');
                }
            }
        });
    }

    function retryJoin() {
        if (!attemptKey || !attemptBody || !(retryReady || (attemptUncertain && sameAttemptRetryAllowed))) return;
        setRetryReady(false);
        sendJoin(attemptKey, attemptBody);
    }

    function startExplicitNewAttempt() {
        if (!attemptUncertain || !riskAcknowledged) return;
        if (attemptScopeKey) removeJoinAttempt(sessionStorage, attemptScopeKey);
        setAttemptKey(null);
        setAttemptBody(null);
        setAttemptUncertain(false);
        setAttemptUncertainReason(null);
        setSameAttemptRetryAllowed(false);
        setRetryReady(false);
    }

    const canJoin = Boolean(attemptScopeReady && attemptScopeKey && !existingParticipant && inviteToken && summary && name.trim() && selectedTripLocation(origin) && locationDisclosureAccepted && !request.loading && !attemptKey && !attemptUncertain && !capacityReached && (!priorAttemptMayHaveSucceeded || riskAcknowledged));
    return <>
        <section className="trip-intro compact-intro"><p className="eyebrow">PRIVATE GUEST INVITATION</p><h1>Join the<br /><em>ride together.</em></h1><p>This link lets you join this trip as one participant.</p></section>
        {existingParticipant ? <section className="trip-panel"><SectionTitle number="01" title="Already joined in this browser" note="Your participant capability is saved for this trip." /><p>Continue with the participant link saved in this browser. This invite will not create another participant.</p>{continuation && <button className="primary-button" onClick={() => navigate(continuation.path)}>{continuation.label}</button>}</section>
            : !inviteToken ? <Notice kind="error">This invitation does not include a guest capability. Reopen the complete private invite link.</Notice>
            : attemptScopeError ? <Notice kind="error">{attemptScopeError}</Notice>
            : !attemptScopeReady ? <Notice>Preparing a private, invite-specific join retry scope…</Notice>
            : summaryError ? <Notice kind="error">{summaryError}</Notice>
                : !summary ? <Notice>Loading the trip summary…</Notice> : <>
                    <section className="trip-summary"><strong>{summary.name}</strong><span>Arrive {formatArrival(summary.arrivalAt, summary.timeZoneId)}</span><span>Destination: {summary.destination.label}</span><span>Local trip time zone: {summary.timeZoneId}</span><small>Arrival time is coordination information, not a routing or arrival guarantee.</small><small>Invitation expires {formatArrival(summary.inviteExpiresAt, summary.timeZoneId)}.</small></section>
                    <Notice>Six people may join. Creating the trip does not use a seat; the organizer can join through this same participant flow.</Notice>
                    {attemptUncertain && <><Notice kind="error">{attemptUncertainReason === 'replay' ? 'This attempt already completed; the replay did not create another participant. Continue with the participant link saved in this browser. If none was saved, access cannot be recovered without an account. Starting another attempt may create a second entry.' : 'The earlier request outcome is unknown. Retry with its same saved UUID and submitted details, or start a separate attempt only after acknowledging duplicate risk.'}</Notice>{sameAttemptRetryAllowed && attemptKey && attemptBody && <button className="secondary-button" type="button" onClick={retryJoin} disabled={request.loading}>{request.loading ? 'Checking the same attempt…' : 'Retry the same saved attempt'}</button>}<label className="inline-check risk-check"><input type="checkbox" checked={riskAcknowledged} onChange={e => setRiskAcknowledged(e.target.checked)} /> I understand a new join attempt is separate and could create a duplicate entry.</label><button className="danger-button" type="button" disabled={!riskAcknowledged} onClick={startExplicitNewAttempt}>Start a separate join attempt</button></>}
                    {capacityReached && <Notice kind="error">The six-person trip limit is reached. Ask the organizer before trying again.</Notice>}
                    {!attemptUncertain && !capacityReached && <>
                        {!hasMapboxToken(mapToken) && <Notice>Mapbox location search is unavailable until a restricted local token is configured. An eligible persistent location is required before joining.</Notice>}
                        <form className="trip-layout" onSubmit={join}>
                            <div className="trip-form-column">
                                {attemptKey && <Notice>A join attempt is saved for this invite. Retrying uses its exact saved request body and UUID.</Notice>}
                                {priorAttemptMayHaveSucceeded && !attemptKey && <Notice kind="error">A join request under an earlier invite may already have added you. Starting this invite creates a separate attempt and may create duplicate membership.</Notice>}
                                <section className="trip-panel"><SectionTitle number="01" title="Your details" note="Only your entry is editable with your personal link." />
                                    <label>Your name<input required maxLength={80} disabled={Boolean(attemptKey)} value={name} onChange={e => edit(() => setName(e.target.value))} placeholder="Name shown to your group" /></label>
                                    <LocationPicker label="Your starting address or venue" value={origin} token={mapToken} disabled={Boolean(attemptKey)} onEdit={place => edit(() => setOrigin(place))} onResolved={place => edit(() => setOrigin(place))} onManualPick={label => setManualTarget({ label, role: 'Participant origin' })} />
                                    {origin.source === 'mapbox-searchbox-temporary' && <Notice kind="error">This Search Box result is temporary and cannot be saved to the trip. Use the manual map pin with a label you provide; permanent geocoding is not enabled.</Notice>}
                                    <div className="preference-box"><label className="inline-check"><input type="checkbox" disabled={Boolean(attemptKey)} checked={canDrive} onChange={e => edit(() => { setCanDrive(e.target.checked); if (!e.target.checked) setPassengerSeats(0); })} /> I can drive a car</label>
                                        {canDrive && <><label>Passenger seats available (driver not included)<input disabled={Boolean(attemptKey)} type="number" min="0" max="8" value={passengerSeats} onChange={e => edit(() => setPassengerSeats(Math.min(8, Math.max(0, Number(e.target.value)))))} /></label><label>Maximum extra estimated vehicle time per driver <span className="range-value">{maxDetourMinutes} min</span><input disabled={Boolean(attemptKey)} type="range" min="0" max="1440" step="5" value={maxDetourMinutes} onChange={e => edit(() => setMaxDetourMinutes(Number(e.target.value)))} /></label><p className="field-note">Measured against the driver's direct estimated trip; excludes independent rider travel.</p></>}
                                        <label className="inline-check"><input type="checkbox" disabled={Boolean(attemptKey)} checked={canMeet} onChange={e => edit(() => setCanMeet(e.target.checked))} /> I'm okay traveling independently to a meetup</label>
                                    </div>
                                </section>
                                <section className="trip-panel disclosure-panel"><h2>Before you share your location</h2><p>Your selected starting location will be visible to the trip organizer and to the carpool companions assigned to your group. It is not shown in the guest invitation summary.</p><label className="inline-check"><input type="checkbox" required disabled={Boolean(attemptKey)} checked={locationDisclosureAccepted} onChange={e => edit(() => setLocationDisclosureAccepted(e.target.checked))} /> I understand who can see my location</label></section>
                                {priorAttemptMayHaveSucceeded && !attemptKey && <label className="inline-check risk-check"><input type="checkbox" checked={riskAcknowledged} onChange={e => setRiskAcknowledged(e.target.checked)} /> I understand this invite is a separate join attempt and the earlier invite may already have added me.</label>}
                                {request.error && <Notice kind="error">{request.error}</Notice>}
                                {!attemptKey && <button className="primary-button" type="submit" disabled={!canJoin}>{request.loading ? 'Joining trip…' : 'Join this trip'}</button>}
                                {attemptKey && !attemptUncertain && retryReady && attemptBody && <><p className="status-note">{retryAfterSeconds > 0 ? `Please wait ${retryAfterSeconds} seconds before the same join attempt can be retried.` : 'Retry will reuse the same idempotency key and exact submitted details.'}</p><button className="primary-button" type="button" onClick={retryJoin} disabled={request.loading || retryAfterSeconds > 0}>{request.loading ? 'Checking the same attempt…' : retryAfterSeconds > 0 ? `Retry in ${retryAfterSeconds}s` : 'Retry the same join attempt'}</button></>}
                            </div>
                            <TripMap token={mapToken} selecting={manualTarget} onPinSelect={point => { if (!manualTarget) return; edit(() => setOrigin({ label: manualTarget.label, latitude: point.latitude, longitude: point.longitude, placeId: null, source: 'user-provided' })); setManualTarget(null); }} locations={[{ id: 'destination', label: 'Shared destination', role: 'Destination', place: destinationFromSummary(summary) }, { id: 'origin', label: 'Your starting place', role: 'Participant origin', place: origin }]} />
                        </form>
                    </>}
                </>}
    </>;
}

function destinationFromSummary(summary: TripSummary): PlaceSelection {
    return demoPlace(summary.destination.label, summary.destination.latitude, summary.destination.longitude);
}
function placeSourceFromProvenance(provenance: JoinRequestBody['origin']['provenance']): PlaceSelection['source'] {
    return provenance === 'userProvided' ? 'user-provided' : provenance === 'mapboxGeocodingPermanent' ? 'mapbox-geocoding-permanent' : 'mapbox-searchbox-temporary';
}
function isJoinCreated(value: unknown): value is { participantId: string; participantCapability: string; revision: number } {
    if (!value || typeof value !== 'object') return false;
    const item = value as Record<string, unknown>;
    return typeof item.participantId === 'string' && typeof item.participantCapability === 'string' && Boolean(item.participantCapability) && typeof item.revision === 'number' && Number.isSafeInteger(item.revision);
}

function defaultArrival() {
    const date = new Date(Date.now() + 2 * 60 * 60 * 1000);
    date.setMinutes(Math.ceil(date.getMinutes() / 5) * 5, 0, 0);
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
function pad(value: number) { return String(value).padStart(2, '0'); }
function formatArrival(value: string, timeZoneId: string) {
    try { return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: timeZoneId }).format(new Date(value)); }
    catch { return value; }
}
function safeSessionGet(key: string) { try { return sessionStorage.getItem(key); } catch { return null; } }
function safeSessionRemove(key: string) { try { sessionStorage.removeItem(key); } catch { /* session storage may be unavailable */ } }
function canStoreCapability() {
    try { const key = 'gather.session-probe'; sessionStorage.setItem(key, '1'); sessionStorage.removeItem(key); return true; }
    catch { return false; }
}
function captureInitialCapability() {
    const { pathname, search, hash } = window.location;
    const stripFragment = (url: string) => window.history.replaceState(null, '', url);
    try { return readCapabilityFragment(pathname, search, hash, window.sessionStorage, stripFragment); }
    catch {
        if (hash) stripFragment(`${pathname}${search}`);
        return { storageError: true };
    }
}
