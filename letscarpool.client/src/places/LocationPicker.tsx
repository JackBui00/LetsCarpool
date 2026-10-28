import { useEffect, useRef, useState, type ChangeEvent, type KeyboardEvent } from 'react';
import { editPlaceLabel, MapboxProviderError, MapboxSearchSequence, MapboxSearchSession, mapboxProviderMessage, requestMapboxLocation, requestMapboxSuggestions, type MapboxSuggestion, type PlaceSelection } from './placeSelection';

type Props = { label: string; value: PlaceSelection; token?: string; disabled?: boolean; onEdit: (value: PlaceSelection) => void; onResolved: (value: PlaceSelection) => void; onManualPick?: (label: string) => void };

export function LocationPicker({ label, value, token, disabled = false, onEdit, onResolved, onManualPick }: Props) {
    const [query, setQuery] = useState(value.label);
    const [suggestions, setSuggestions] = useState<MapboxSuggestion[]>([]);
    const [activeIndex, setActiveIndex] = useState(-1);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');
    const [searchEnabled, setSearchEnabled] = useState(false);
    const [manualLabel, setManualLabel] = useState('');
    const [manualPickRequested, setManualPickRequested] = useState(false);
    const searchSession = useRef(new MapboxSearchSession());
    const generation = useRef(new MapboxSearchSequence());

    useEffect(() => {
        if (value.source !== 'unresolved' && value.label !== query) {
            setQuery(value.label);
            setSearchEnabled(false);
        }
    }, [query, value.label, value.source]);

    useEffect(() => {
        if (!token || !searchEnabled || query.trim().length < 3) {
            setSuggestions([]);
            setLoading(false);
            return;
        }
        const requestId = generation.current.begin();
        const controller = new AbortController();
        const timer = window.setTimeout(async () => {
            setLoading(true);
            setError('');
            try {
                const results = await requestMapboxSuggestions(token, query.trim(), searchSession.current.current(), controller.signal);
                if (!generation.current.isCurrent(requestId)) return;
                setSuggestions(results);
                setActiveIndex(-1);
            } catch (reason) {
                if (!controller.signal.aborted && generation.current.isCurrent(requestId)) {
                    setSuggestions([]);
                    setError(reason instanceof MapboxProviderError ? mapboxProviderMessage(reason.status) : 'Map search is temporarily unavailable. Check the network and try again.');
                }
            } finally {
                if (generation.current.isCurrent(requestId)) setLoading(false);
            }
        }, 350);
        return () => { window.clearTimeout(timer); controller.abort(); };
    }, [query, searchEnabled, token]);

    function changeQuery(event: ChangeEvent<HTMLInputElement>) {
        const next = event.target.value;
        generation.current.invalidate();
        setQuery(next);
        setSearchEnabled(true);
        setSuggestions([]);
        setActiveIndex(-1);
        setError('');
        setManualLabel('');
        onEdit(editPlaceLabel(next));
    }

    async function select(suggestion: MapboxSuggestion) {
        if (!token) return;
        const requestId = generation.current.begin();
        const currentSession = searchSession.current.current();
        setLoading(true);
        setError('');
        setSuggestions([]);
        try {
            const location = await requestMapboxLocation(suggestion.mapbox_id, token, currentSession);
            if (!generation.current.isCurrent(requestId) || !location) {
                if (generation.current.isCurrent(requestId)) setError('That search result did not include a usable location. Choose another result.');
                return;
            }
            setQuery(location.label);
            setSearchEnabled(false);
            onResolved({ label: location.label, latitude: location.latitude, longitude: location.longitude, placeId: location.mapboxId || null, source: 'mapbox-searchbox-temporary' });
            searchSession.current.complete();
        } catch (reason) {
            if (generation.current.isCurrent(requestId)) setError(reason instanceof MapboxProviderError ? mapboxProviderMessage(reason.status) : 'Map search is temporarily unavailable. Check the network and try again.');
        } finally {
            if (generation.current.isCurrent(requestId)) setLoading(false);
        }
    }

    function keyDown(event: KeyboardEvent<HTMLInputElement>) {
        if (event.key === 'ArrowDown' && suggestions.length) { event.preventDefault(); setActiveIndex(index => (index + 1) % suggestions.length); }
        else if (event.key === 'ArrowUp' && suggestions.length) { event.preventDefault(); setActiveIndex(index => (index - 1 + suggestions.length) % suggestions.length); }
        else if (event.key === 'Enter' && activeIndex >= 0) { event.preventDefault(); void select(suggestions[activeIndex]); }
        else if (event.key === 'Escape') setSuggestions([]);
    }

    const selectedTemporary = value.source === 'mapbox-searchbox-temporary';
    const resolved = value.source !== 'unresolved' && value.latitude !== null && value.longitude !== null;
    const listId = `location-results-${label.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;
    return <div className="location-picker">
        <label htmlFor={`${listId}-input`}>{label}</label>
        <div className="location-search-wrap">
            <input id={`${listId}-input`} role="combobox" aria-autocomplete="list" aria-expanded={suggestions.length > 0} aria-controls={listId} aria-activedescendant={activeIndex >= 0 ? `${listId}-${activeIndex}` : undefined} disabled={disabled || !token} value={query} onChange={changeQuery} onKeyDown={keyDown} placeholder={token ? 'Search an address, venue, or landmark' : 'Map search setup required'} />
            {suggestions.length > 0 && <ul id={listId} className="location-suggestions" role="listbox">{suggestions.map((item, index) => <li id={`${listId}-${index}`} role="option" aria-selected={index === activeIndex} key={item.mapbox_id}><button type="button" tabIndex={-1} onMouseDown={event => event.preventDefault()} onClick={() => void select(item)}><strong>{item.name}</strong><span>{item.full_address || item.place_formatted}</span></button></li>)}</ul>}
        </div>
        <span className={resolved ? 'location-status resolved' : 'location-status'}>{loading ? 'Searching Mapbox…' : resolved ? value.source === 'demo' ? 'Demo location · replace with a searched place' : selectedTemporary ? 'Selected search result · temporary use only' : 'Location selected' : token ? 'Choose a search result to set this location' : 'Map search is not configured'}</span>
        {!token && <span className="location-error" role="status">Map search is unavailable until a local Mapbox token is configured. Unresolved locations cannot be submitted.</span>}
        {selectedTemporary && <span className="location-error" role="status">Search results are temporary and cannot be saved to a trip. Use the manual map pin with a label you provide; permanent geocoding is not enabled.</span>}
        {error && <span className="location-error" role="alert">{error}</span>}
        {onManualPick && <div className="manual-pin-control"><label htmlFor={`${listId}-manual`}>Or name a point you place yourself<input id={`${listId}-manual`} maxLength={120} disabled={disabled || !token} value={manualLabel} onChange={event => { generation.current.invalidate(); setManualLabel(event.target.value); setManualPickRequested(false); setQuery(''); setSearchEnabled(false); setSuggestions([]); setError(''); onEdit(editPlaceLabel('')); }} placeholder="A label you provide" /></label><button type="button" className="secondary-button" disabled={disabled || !token || !manualLabel.trim()} onClick={() => { setManualPickRequested(true); onManualPick(manualLabel.trim()); }}>{manualPickRequested ? 'Choose a different point' : 'Choose point on map'}</button>{manualPickRequested && <p className="map-pick-hint" role="status">Click the map to place: {manualLabel}</p>}<small>This location uses only your label and map pin; no address is inferred.</small></div>}
        <small className="mapbox-attribution">Search results powered by Mapbox</small>
    </div>;
}
