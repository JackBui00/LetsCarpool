import { useEffect, useRef, useState } from 'react';
import { editPlaceLabel, loadGoogleMaps, type GooglePlace, type PlaceSelection } from './placeSelection';

type Props = { label: string; value: PlaceSelection; apiKey?: string; onEdit: (value: PlaceSelection) => void; onResolved: (value: PlaceSelection) => void };

export function LocationPicker({ label, value, apiKey, onEdit, onResolved }: Props) {
    const host = useRef<HTMLDivElement>(null);
    const generation = useRef(0);
    const lastWidgetValue = useRef(value.label);
    const [error, setError] = useState('');
    const [unavailable, setUnavailable] = useState(false);
    const [ready, setReady] = useState(false);

    useEffect(() => {
        if (!apiKey?.trim() || !host.current) return;
        let active = true;
        let dispose = () => {};
        loadGoogleMaps(apiKey).then(async maps => {
            if (!active || !host.current) return;
            try {
                const places = await maps.importLibrary('places') as { PlaceAutocompleteElement: new (options: { includedPrimaryTypes: string[] }) => HTMLElement & { value: string } };
                if (!active || !host.current) return;
                const widget = new places.PlaceAutocompleteElement({ includedPrimaryTypes: ['geocode', 'establishment'] });
                widget.setAttribute('aria-label', `Search ${label.toLowerCase()}`);
                widget.setAttribute('placeholder', `Search an address or ${label.toLowerCase()}`);
                widget.value = value.label;
                lastWidgetValue.current = value.label;
                const invalidate = () => {
                    generation.current += 1;
                    lastWidgetValue.current = widget.value;
                    setError('');
                    onEdit(editPlaceLabel(widget.value));
                };
                const onInput = () => {
                    if (widget.value !== lastWidgetValue.current) invalidate();
                };
                const onKeyDown = (event: Event) => {
                    const key = (event as KeyboardEvent).key;
                    const modifiesText = key.length === 1
                        || key === 'Backspace'
                        || key === 'Delete'
                        || ((event as KeyboardEvent).ctrlKey || (event as KeyboardEvent).metaKey) && ['v', 'x'].includes(key.toLowerCase());
                    if (modifiesText) invalidate();
                };
                const onSelect = async (event: Event) => {
                    event.stopPropagation();
                    const request = ++generation.current;
                    onEdit(editPlaceLabel(widget.value));
                    try {
                        const prediction = (event as CustomEvent<{ placePrediction: { toPlace: () => GooglePlace } }>).detail?.placePrediction
                            ?? (event as CustomEvent & { placePrediction: { toPlace: () => GooglePlace } }).placePrediction;
                        const place = prediction.toPlace();
                        await place.fetchFields({ fields: ['displayName', 'formattedAddress', 'location'] });
                        if (!active || request !== generation.current || !place.location) return;
                        const formattedAddress = place.formattedAddress || place.displayName || widget.value;
                        widget.value = formattedAddress;
                        lastWidgetValue.current = formattedAddress;
                        onResolved({ label: formattedAddress, latitude: place.location.lat(), longitude: place.location.lng(), placeId: place.id ?? null, source: 'google' });
                    } catch {
                        if (active && request === generation.current) setError('That place could not be resolved. Choose another search result.');
                    }
                };
                widget.addEventListener('input', onInput);
                widget.addEventListener('keydown', onKeyDown);
                widget.addEventListener('gmp-select', onSelect);
                widget.addEventListener('gmp-error', () => { if (active) setUnavailable(true); }, { once: true });
                host.current.replaceChildren(widget);
                setReady(true);
                dispose = () => {
                    widget.removeEventListener('input', onInput);
                    widget.removeEventListener('keydown', onKeyDown);
                    widget.removeEventListener('gmp-select', onSelect);
                    widget.remove();
                };
            } catch {
                if (active) setUnavailable(true);
            }
        }).catch(() => { if (active) setUnavailable(true); });
        return () => { active = false; dispose(); };
    // Initialize the widget once per picker; user edits are owned by the custom element.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [apiKey]);

    const resolved = value.source !== 'unresolved' && value.latitude !== null && value.longitude !== null;
    return <div className="location-picker">
        <label>{label}<div ref={host} className="places-widget-host" />
            {!apiKey?.trim() && <input aria-label={label} value={value.label} onChange={event => onEdit(editPlaceLabel(event.target.value))} placeholder="Location search setup required" />}
            {apiKey?.trim() && !ready && !unavailable && <input aria-label={label} value={value.label} readOnly placeholder="Loading Google location search…" />}
            {apiKey?.trim() && unavailable && <input aria-label={label} value={value.label} readOnly placeholder="Location search unavailable" />}
        </label>
        <span className={resolved ? 'location-status resolved' : 'location-status'}>{resolved ? value.source === 'demo' ? 'Demo location · replace with a searched place' : 'Selected from Google Places' : 'Choose a search result to set this location'}</span>
        {unavailable && <span className="location-error" role="status">Google location search is unavailable. Check network access and key restrictions.</span>}
        {error && <span className="location-error" role="alert">{error}</span>}
    </div>;
}
