import { useEffect, useRef, useState } from 'react';
import { loadGoogleMaps, type Coordinate, type PlaceSelection } from './placeSelection';

type MarkerLocation = { id: string; label: string; role: 'Destination' | 'Participant origin'; place: PlaceSelection };
type GoogleMapInstance = { fitBounds: (bounds: unknown, padding: number) => void; setCenter: (center: Coordinate) => void };
type MapsLibrary = { Map: new (node: HTMLElement, options: { center: Coordinate; zoom: number; mapId: string; mapTypeControl: boolean; streetViewControl: boolean; fullscreenControl: boolean }) => GoogleMapInstance };
type MarkerLibrary = { AdvancedMarkerElement: new (options: { map: GoogleMapInstance; position: Coordinate; title: string; content: HTMLElement }) => { map: GoogleMapInstance | null } };

export function TripMap({ apiKey, locations }: { apiKey?: string; locations: MarkerLocation[] }) {
    const mapNode = useRef<HTMLDivElement>(null);
    const map = useRef<GoogleMapInstance | null>(null);
    const markers = useRef<Array<{ map: GoogleMapInstance | null }>>([]);
    const [error, setError] = useState(false);
    const [mapReady, setMapReady] = useState(false);
    const resolved = locations.filter(item => item.place.latitude !== null && item.place.longitude !== null);
    const markerPayload = JSON.stringify(resolved.map(item => ({ ...item, place: { ...item.place, latitude: item.place.latitude!, longitude: item.place.longitude! } })));

    useEffect(() => {
        if (!apiKey?.trim() || !mapNode.current) return;
        let active = true;
        loadGoogleMaps(apiKey).then(async maps => {
            if (!active || !mapNode.current) return;
            try {
                const library = await maps.importLibrary('maps') as MapsLibrary;
                if (!active || !mapNode.current) return;
                map.current ??= new library.Map(mapNode.current, {
                    center: { lat: 37.7694, lng: -122.4262 }, zoom: 12,
                    mapId: 'DEMO_MAP_ID', mapTypeControl: false, streetViewControl: false,
                    fullscreenControl: false,
                });
                setMapReady(true);
            } catch { if (active) setError(true); }
        }).catch(() => { if (active) setError(true); });
        return () => { active = false; };
    }, [apiKey]);

    useEffect(() => {
        let active = true;
        const currentMap = map.current;
        const markerLocations = JSON.parse(markerPayload) as Array<MarkerLocation & { place: PlaceSelection & { latitude: number; longitude: number } }>;
        if (!currentMap || markerLocations.length === 0) return;
        loadGoogleMaps(apiKey).then(async maps => {
            const library = await maps.importLibrary('marker') as MarkerLibrary;
            if (!active) return;
            markers.current.forEach(marker => marker.map = null);
            markers.current = markerLocations.map((item, index) => {
                const marker = new library.AdvancedMarkerElement({
                    map: currentMap,
                    position: { lat: item.place.latitude, lng: item.place.longitude },
                    title: `${item.role}: ${item.label}`,
                    content: markerContent(item.role, index),
                });
                return marker;
            });
            const bounds = new maps.LatLngBounds();
            markerLocations.forEach(item => bounds.extend({ lat: item.place.latitude, lng: item.place.longitude }));
            if (markerLocations.length > 1) currentMap.fitBounds(bounds, 42);
            else currentMap.setCenter(bounds.getCenter());
        }).catch(() => { if (active) setError(true); });
        return () => { active = false; };
    }, [apiKey, markerPayload, mapReady]);

    return <aside className="map-panel" aria-label="Trip location map">
        <div className="map-heading"><div><strong>Trip locations</strong><span>Distinct pins show selected origins and the destination.</span></div><details className="map-legend"><summary>Legend</summary><p><span className="legend-pin destination-pin">D</span> Destination</p><p><span className="legend-pin participant-pin">1</span> Participant origins</p></details></div>
        {!apiKey?.trim() ? <div className="map-placeholder"><strong>Map preview setup needed</strong><span>Add a restricted Google Maps key to enable the map and address search. Demo pin data is not a live map.</span><small>Google Maps attribution appears here when enabled.</small></div> : error ? <div className="map-placeholder" role="status"><strong>Google Maps is unavailable</strong><span>Check network access, key restrictions, and Maps JavaScript API access.</span></div> : <div ref={mapNode} className="google-map" aria-label={`${resolved.length} selected trip locations`} />}
        <div className="map-attribution">Map data © Google</div>
    </aside>;
}

function markerContent(role: MarkerLocation['role'], index: number) {
    const marker = document.createElement('span');
    marker.className = role === 'Destination' ? 'map-marker destination-marker' : 'map-marker participant-marker';
    marker.textContent = role === 'Destination' ? 'D' : String(index);
    marker.setAttribute('aria-label', role);
    return marker;
}
