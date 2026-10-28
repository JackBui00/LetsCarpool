import { useRef, useState, type FormEvent } from 'react';
import { PlanRequestRevision, runPlanRequest } from './planRequestLifecycle.ts';
import { demoPlace, hasMapboxToken, isPlaceResolved, isPlanReady, unresolvedPlace, type PlaceSelection } from './places/placeSelection';
import { LocationPicker } from './places/LocationPicker';
import { TripMap } from './places/TripMap';
import './App.css';

type Place = PlaceSelection;
type Participant = { id: string; name: string; origin: Place; passengerSeats: number; canMeet: boolean };
type Stop = { participantId: string | null; label: string; latitude: number; longitude: number; kind: 'pickup' | 'rendezvous' | 'destination' };
type TravelLocation = { participantId: string; label: string; latitude: number; longitude: number; kind: 'origin' | 'rendezvous' };
type TravelLeg = { participantId: string; from: TravelLocation; to: TravelLocation; distanceMeters: number; durationMinutes: number; modeAssumption: 'unspecified' | string };
type Group = { driver: { id: string; name: string }; riders: Array<{ id: string; name: string }>; stops: Stop[]; independentTravelLegs: TravelLeg[]; vehicleDistanceMeters: number; independentTravelDistanceMeters: number; totalDistanceMeters: number; durationMinutes: number; driverDetourMinutes: number };
type Alternative = { strategy: 'pickup' | 'rendezvous'; groups: Group[]; totalDistanceMeters: number; durationMinutes: number; unassigned: Array<{ participantId: string; reason: string }>; distanceSource: 'haversine-estimate'; optimality: 'exact-within-limit' | string; warnings: string[] };
type PlanResponse = { alternatives: Alternative[]; searchOperations: number; searchOperationLimit: number };
type ProblemDetails = { type?: string; title?: string; detail?: string; status?: number; message?: string; errors?: Record<string, string[]> };

const seed: Participant[] = [
    { id: 'alex', name: 'Alex', origin: demoPlace('Mission Dolores Park', 37.7596, -122.4269), passengerSeats: 3, canMeet: true },
    { id: 'sam', name: 'Sam', origin: demoPlace('Painted Ladies', 37.7763, -122.4329), passengerSeats: 0, canMeet: true },
    { id: 'jordan', name: 'Jordan', origin: demoPlace('Ferry Building', 37.7955, -122.3937), passengerSeats: 0, canMeet: false },
];
const initialDestination: Place = demoPlace('Golden Gate Park', 37.7694, -122.4862);
const mapToken = import.meta.env.VITE_MAPBOX_ACCESS_TOKEN;

function App() {
    const [destination, setDestination] = useState(initialDestination);
    const [participants, setParticipants] = useState(seed);
    const [maxDetourMinutes, setMaxDetourMinutes] = useState(30);
    const [result, setResult] = useState<PlanResponse | null>(null);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(false);
    const [manualTarget, setManualTarget] = useState<{ id: string; label: string; role: 'Destination' | 'Participant origin' } | null>(null);
    const planRevisions = useRef(new PlanRequestRevision());

    function invalidatePlan() {
        planRevisions.current.invalidate();
        setResult(null);
        setError('');
        setLoading(false);
    }

    function updateParticipant(id: string, change: Partial<Participant> | { origin: Partial<Place> }) {
        invalidatePlan();
        setParticipants(current => current.map(person => person.id === id
            ? { ...person, ...change, origin: 'origin' in change ? { ...person.origin, ...change.origin } : person.origin }
            : person));
    }

    function addParticipant() {
        if (participants.length >= 6) return;
        invalidatePlan();
        const id = `friend-${Date.now()}`;
        setParticipants(current => [...current, { id, name: '', origin: unresolvedPlace(''), passengerSeats: 0, canMeet: false }]);
    }

    async function planTrip(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (!isPlanReady(mapToken, [destination, ...participants.map(person => person.origin)])) return;
        setError('');
        setResult(null);
        setLoading(true);
        const requestRevision = planRevisions.current.begin();
        await runPlanRequest(planRevisions.current, requestRevision, async () => {
            const response = await fetch('/api/v1/carpool/plan', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ destination: toApiPlace(destination), participants: participants.map(person => ({ ...person, origin: toApiPlace(person.origin) })), maxDetourMinutes }),
            });
            const payload = await response.json().catch(() => null) as PlanResponse | ProblemDetails | null;
            if (!response.ok) {
                const details = payload && 'errors' in payload && payload.errors
                    ? Object.entries(payload.errors).flatMap(([field, messages]) => messages.map(message => `${field}: ${message}`)).join(' ')
                    : '';
                throw new Error(details || (payload && 'detail' in payload && payload.detail) || (payload && 'message' in payload && payload.message) || (payload && 'title' in payload && payload.title) || `Planner request failed (${response.status}).`);
            }
            if (!payload || !('alternatives' in payload)) throw new Error('The planner response was missing its alternatives. Please try again.');
            return payload;
        }, {
            onSuccess: setResult,
            onError: reason => setError(reason instanceof Error ? reason.message : 'Could not reach the trip planner. Check the connection and try again.'),
            onFinally: () => setLoading(false),
        });
    }

    const patchDestination = (place: Place) => {
        invalidatePlan();
        setDestination(place);
    };
    const planReady = isPlanReady(mapToken, [destination, ...participants.map(person => person.origin)]);
    return <main className="app-shell">
        <header className="topbar"><a className="brand" href="#top" aria-label="Gather home"><span className="brand-mark" aria-hidden="true">g</span> gather<span className="brand-dot">.</span></a><span className="session-badge"><span /> Unsaved planning session</span></header>
        <section className="intro" id="top"><p className="eyebrow">LESS COORDINATION. MORE TOGETHER.</p><h1>Getting there is<br /><em>part of the plan.</em></h1><p className="intro-copy">Bring your crew together. Compare picking each other up with meeting along the way.</p></section>
        <form onSubmit={planTrip}>
            {!hasMapboxToken(mapToken) && <div className="setup-notice" role="status"><strong>Mapbox location search unavailable</strong><span>Configure a restricted public Mapbox token in the ignored local <code>.env.local</code> file. Until then, the prefilled locations are demo data and cannot be submitted. Mapbox usage is metered; a free allowance is not a spending cap.</span></div>}
            <div className="planner-layout">
            <div className="planner-form">
            <section className="panel destination-panel" aria-labelledby="destination-title"><div className="section-heading"><span className="step">01</span><div><h2 id="destination-title">Where are you all headed?</h2><p>One shared destination keeps everyone on the same trip.</p></div></div>
                <LocationPicker label="Destination address or venue" value={destination} token={mapToken} onEdit={patchDestination} onResolved={place => { invalidatePlan(); setDestination(place); }} onManualPick={label => setManualTarget({ id: 'destination', label, role: 'Destination' })} />
            </section>
            <section className="panel people-panel" aria-labelledby="people-title"><div className="section-heading"><span className="step">02</span><div><h2 id="people-title">Who's coming along?</h2><p>Add your friends and where they'll start from. Up to 6 people.</p></div><span className="people-count">{participants.length} of 6</span></div>
                <div className="people-list">{participants.map((person, index) => <article className="person-card" key={person.id}><div className="person-head"><span className="person-number">{String(index + 1).padStart(2, '0')}</span><label className="name-field">Name<input required value={person.name} onChange={e => updateParticipant(person.id, { name: e.target.value })} placeholder="Friend's name" /></label><button className="remove-button" type="button" aria-label={`Remove ${person.name || 'participant'}`} onClick={() => { invalidatePlan(); setParticipants(current => current.filter(p => p.id !== person.id)); }} disabled={participants.length <= 1}>Remove</button></div>
                    <LocationPicker label={`${person.name || 'Participant'} starting address or venue`} value={person.origin} token={mapToken} onEdit={place => updateParticipant(person.id, { origin: place })} onResolved={place => updateParticipant(person.id, { origin: place })} onManualPick={label => setManualTarget({ id: person.id, label, role: 'Participant origin' })} />
                    <div className="person-options"><label className="seat-control">Passenger seats <input aria-label={`Passenger seats offered by ${person.name || 'participant'}`} type="number" min="0" max="8" value={person.passengerSeats} onChange={e => updateParticipant(person.id, { passengerSeats: Math.min(8, Math.max(0, Number(e.target.value))) })} /></label><span className="seat-help">Seats available for friends, driver not included</span><label className="check-control"><input type="checkbox" checked={person.canMeet} onChange={e => updateParticipant(person.id, { canMeet: e.target.checked })} /><span>I'm okay traveling to meet my driver</span></label></div>
                </article>)}</div>
                <button className="add-button" type="button" onClick={addParticipant} disabled={participants.length >= 6}><span aria-hidden="true">+</span> {participants.length >= 6 ? 'Maximum group size reached' : 'Add a friend'}</button>
            </section>
            </div>
            <TripMap token={mapToken} selecting={manualTarget} onPinSelect={point => {
                if (!manualTarget) return;
                const place: PlaceSelection = { label: manualTarget.label, latitude: point.latitude, longitude: point.longitude, placeId: null, source: 'user-provided' };
                if (manualTarget.id === 'destination') { invalidatePlan(); setDestination(place); }
                else updateParticipant(manualTarget.id, { origin: place });
                setManualTarget(null);
            }} locations={[{ id: 'destination', label: destination.label, role: 'Destination', place: destination }, ...participants.map((person, index) => ({ id: person.id, label: `${index + 1}. ${person.name || 'Participant'}`, role: 'Participant origin' as const, place: person.origin }))]} />
            </div>
            <section className="panel preferences-panel" aria-labelledby="preferences-title"><div className="section-heading"><span className="step">03</span><div><h2 id="preferences-title">Set the ride preferences</h2><p>Limit extra estimated time on each driver's vehicle route.</p></div></div><label className="detour-control">Maximum extra estimated vehicle time per driver <span className="range-value">{maxDetourMinutes} min</span><input type="range" min="0" max="1440" step="5" value={maxDetourMinutes} onChange={e => { invalidatePlan(); setMaxDetourMinutes(Number(e.target.value)); }} /><span className="range-ends"><span>No extra time</span><span>24 hours</span></span></label><p className="field-note detour-note">Measured beyond the driver's direct origin-to-destination estimate. Includes the driver's route through pickups or a meetup; excludes riders' independent travel to rendezvous points.</p></section>
            <div className="submit-row"><p>{planReady ? 'Compare shared pickup routes and meeting points.' : !hasMapboxToken(mapToken) ? 'Set up Mapbox search to resolve demo places and plan.' : 'Choose a search result for every location before planning.'}</p><button className="primary-button" type="submit" disabled={loading || !planReady}>{loading ? <><span className="spinner" /> Finding options…</> : <>Find our options <span aria-hidden="true">→</span></>}</button></div>
        </form>
        {error && <div className="notice error-notice" role="alert"><strong>We couldn't plan this trip.</strong><span>{error}</span></div>}
        {result && <Results response={result} participants={participants} />}
        <footer className="page-footer"><span>Made for the ride together.</span><span>Planning only · no saved trips yet</span></footer>
    </main>;
}

function Results({ response, participants }: { response: PlanResponse; participants: Participant[] }) {
    const alternatives = response.alternatives ?? [];
    const participantNames = new Map(participants.map(person => [person.id, person.name]));
    return <section className="results" aria-live="polite" aria-labelledby="results-title">
        <div className="results-heading"><div><p className="eyebrow">YOUR CREW, CONNECTED</p><h2 id="results-title">Trip options</h2></div><span className="estimate-tag">Estimates only</span></div>
        <p className="results-objective">Options prioritize getting the most people to the shared destination, then compare total travel: vehicle routes plus independent rider legs.</p>
        {alternatives.length === 0 ? <div className="notice empty-notice"><strong>No workable option found yet.</strong><span>Try adding a driver, offering more passenger seats, or allowing friends to meet at a pickup point.</span></div> : alternatives.map((alternative, index) => <article className="alternative-card" key={alternative.strategy + "-" + index}>
            <div className="alternative-top"><div><span className="option-label">OPTION {index + 1}</span><h3>{formatStrategy(alternative.strategy)}</h3></div><div className="option-stats"><strong>{formatDistance(alternative.totalDistanceMeters)}</strong><span>Estimated total travel</span><strong>{alternative.durationMinutes} min</strong><span>Estimated time</span></div></div>
            {alternative.groups.map((group, groupIndex) => <div className="group-block" key={group.driver.id + "-" + groupIndex}>
                <h4><span className="car-icon" aria-hidden="true">↗</span> {group.driver.name} drives <small>{group.riders.length} {group.riders.length === 1 ? "rider" : "riders"}</small></h4>
                <p className="group-riders"><strong>Riders:</strong> {group.riders.map(rider => rider.name).join(", ") || "Driver only"}</p>
                <div className="group-distance"><span>Vehicle travel <strong>{formatDistance(group.vehicleDistanceMeters)}</strong></span><span>Driver detour <strong>{group.driverDetourMinutes} min</strong></span><span>Independent rider travel <strong>{formatDistance(group.independentTravelDistanceMeters)}</strong></span><span>Total travel <strong>{formatDistance(group.totalDistanceMeters)}</strong></span><span>Estimated time <strong>{group.durationMinutes} min</strong></span></div>
                <ol className="stop-list">{group.stops.map((stop, stopIndex) => {
                    const stopPerson = stop.participantId ? participantNames.get(stop.participantId) ?? stop.participantId : null;
                    const heading = stop.kind === "rendezvous" ? "Meetup at " + (stopPerson ?? "selected origin") + "'s origin" : stop.kind === "destination" ? "Shared destination" : stopPerson ?? "Pickup stop";
                    return <li key={(stop.participantId ?? stop.kind) + "-" + stopIndex}><span className="stop-dot" /><span className="stop-person">{heading}</span><span>{stop.label}</span><span className="stop-kind">{formatStopKind(stop.kind)}</span></li>;
                })}</ol>
                {group.independentTravelLegs.length > 0 && <div className="travel-legs"><h5>Independent trips to the meetup</h5>{group.independentTravelLegs.map((leg, legIndex) => <p key={leg.participantId + "-" + legIndex}><strong>{participantNames.get(leg.participantId) ?? leg.participantId}:</strong> {leg.from.label} → {leg.to.label} · estimated distance {formatDistance(leg.distanceMeters)} · estimated {leg.durationMinutes} min ({formatModeAssumption(leg.modeAssumption)}; 40 km/h Haversine estimate)</p>)}</div>}
            </div>)}
            {alternative.unassigned.length > 0 && <div className="unassigned"><h4>Needs another plan</h4>{alternative.unassigned.map(person => <p key={person.participantId}><strong>{participantNames.get(person.participantId) ?? person.participantId}</strong> · {person.reason}</p>)}</div>}
            <p className="method-note">{formatLabel(alternative.distanceSource)} · {formatOptimality(alternative.optimality)} · Mixed per-group strategies are not searched. Search operations: {response.searchOperations.toLocaleString()} / {response.searchOperationLimit.toLocaleString()}.</p>
            {alternative.warnings?.map((warning, warningIndex) => <p className="warning" key={warningIndex}>{warning}</p>)}
        </article>)}
        <p className="results-disclaimer">Distances are straight-line estimates, not road routes. Durations sum leg estimates at 40 km/h; traffic and turn-by-turn navigation are not included. Arrival deadlines and windows are not enforced.</p>
    </section>;
}
function formatStrategy(value: string) { return value === 'rendezvous' ? 'Meet along the way' : value === 'pickup' ? 'Pick up along the way' : formatLabel(value); }
function formatStopKind(value: string) { return value === 'rendezvous' ? 'Meetup point' : value === 'destination' ? 'Shared destination' : formatLabel(value); }
function formatLabel(value: string) { return value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[-_]/g, ' ').replace(/^./, letter => letter.toUpperCase()); }
function formatModeAssumption(value: string) { return value === 'unspecified' ? 'mode unspecified' : `${formatLabel(value)} mode`; }
function formatOptimality(value: string) { return value === 'exact-within-limit' ? 'Exact among listed pickup and meetup strategies (up to 6 people)' : formatLabel(value); }
function formatDistance(meters: number) { return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${Math.round(meters)} m`; }
function toApiPlace(place: Place) {
    if (!isPlaceResolved(place)) throw new Error('Choose a location search result before planning this trip.');
    return { label: place.label, latitude: place.latitude!, longitude: place.longitude! };
}
export default App;
