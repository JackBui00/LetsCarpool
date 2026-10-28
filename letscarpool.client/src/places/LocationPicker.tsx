import { useEffect, useState, type ChangeEvent } from 'react';
import { editPlaceLabel, type PlaceSelection } from './placeSelection';

type Props = {
    label: string;
    value: PlaceSelection;
    token?: string;
    disabled?: boolean;
    onEdit: (value: PlaceSelection) => void;
    onManualPick?: (label: string) => void;
};

export function LocationPicker({ label, value, token, disabled = false, onEdit, onManualPick }: Props) {
    const [manualLabel, setManualLabel] = useState('');
    const [manualPickRequested, setManualPickRequested] = useState(false);
    const resolved = value.source !== 'unresolved' && value.latitude !== null && value.longitude !== null;
    const listId = `location-results-${label.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`;

    useEffect(() => {
        if (value.source === 'unresolved' || value.label === manualLabel) return;
        setManualLabel('');
        setManualPickRequested(false);
    }, [manualLabel, value.label, value.source]);

    function editManualLabel(event: ChangeEvent<HTMLInputElement>) {
        const next = event.target.value;
        setManualLabel(next);
        setManualPickRequested(false);
        onEdit(editPlaceLabel(''));
    }

    return <div className="location-picker">
        <label htmlFor={`${listId}-input`}>{label}</label>
        <input id={`${listId}-input`} disabled value={resolved ? value.label : ''} placeholder="Address and venue search unavailable" aria-describedby={`${listId}-status`} />
        <span id={`${listId}-status`} className={resolved ? 'location-status resolved' : 'location-status'} role="status">
            {resolved ? 'Location selected' : 'Address and venue search is unavailable until permanent storage is eligible and configured.'}
        </span>
        {!token && <span className="location-error" role="status">The map is unavailable until a restricted local Mapbox map token is configured.</span>}
        {onManualPick && <div className="manual-pin-control">
            <label htmlFor={`${listId}-manual`}>Enter your own location label
                <input id={`${listId}-manual`} maxLength={120} disabled={disabled || !token} value={manualLabel} onChange={editManualLabel} placeholder="A label you provide" />
            </label>
            <button type="button" className="secondary-button" disabled={disabled || !token || !manualLabel.trim()} onClick={() => { setManualPickRequested(true); onManualPick(manualLabel.trim()); }}>
                {manualPickRequested ? 'Choose a different point' : 'Choose point on map'}
            </button>
            {manualPickRequested && <p className="map-pick-hint" role="status">Click the map to place: {manualLabel}</p>}
            <small>This is a user-provided label and pin. No address is inferred from the map.</small>
        </div>}
    </div>;
}
