import { useEffect, useRef, useState } from 'react';
import type { Map as MapboxMap, MapMouseEvent, Marker as MapboxMarker } from 'mapbox-gl';
import 'mapbox-gl/dist/mapbox-gl.css';
import type { PlaceSelection } from './placeSelection';

type MarkerLocation = { id: string; label: string; role: 'Destination' | 'Participant origin'; place: PlaceSelection };
type Props = { token?: string; locations: MarkerLocation[]; selecting?: { label: string; role: 'Destination' | 'Participant origin' } | null; onPinSelect?: (location: { latitude: number; longitude: number }) => void };

export function TripMap({ token, locations, selecting, onPinSelect }: Props) {
    const mapNode = useRef<HTMLDivElement>(null);
    const map = useRef<MapboxMap | null>(null);
    const api = useRef<typeof import('mapbox-gl').default | null>(null);
    const markers = useRef<MapboxMarker[]>([]);
    const [error, setError] = useState(false);
    const [mapReady, setMapReady] = useState(false);
    const resolved = locations.filter(item => item.place.latitude !== null && item.place.longitude !== null);
    const markerPayload = JSON.stringify(resolved.map(item => ({ ...item, place: { ...item.place, latitude: item.place.latitude!, longitude: item.place.longitude! } })));

    useEffect(() => {
        if (!token?.trim() || !mapNode.current) return;
        let active = true;
        let resizeObserver: ResizeObserver | undefined;
        void import('mapbox-gl').then(mapboxgl => {
            if (!active || !mapNode.current) return;
            mapboxgl.default.accessToken = token;
            api.current = mapboxgl.default;
            const instance = new mapboxgl.default.Map({
                container: mapNode.current,
                style: 'mapbox://styles/mapbox/streets-v12',
                center: [-122.42, 37.77],
                zoom: 11,
                attributionControl: true,
                cooperativeGestures: true,
            });
            map.current = instance;
            instance.once('load', () => { if (active) setMapReady(true); });
            instance.on('error', () => { if (active) setError(true); });
            resizeObserver = new ResizeObserver(() => instance.resize());
            resizeObserver.observe(mapNode.current);
        }).catch(() => { if (active) setError(true); });
        return () => {
            active = false;
            resizeObserver?.disconnect();
            markers.current.forEach(marker => marker.remove());
            markers.current = [];
            map.current?.remove();
            map.current = null;
            api.current = null;
            setMapReady(false);
        };
    }, [token]);

    useEffect(() => {
        const currentMap = map.current;
        if (!currentMap || !mapReady) return;
        markers.current.forEach(marker => marker.remove());
        markers.current = [];
        const markerLocations = JSON.parse(markerPayload) as Array<MarkerLocation & { place: PlaceSelection & { latitude: number; longitude: number } }>;
        const mapboxgl = api.current;
        if (!mapboxgl) return;
        markers.current = markerLocations.map((item, index) => {
                const node = document.createElement('span');
                node.className = item.role === 'Destination' ? 'map-marker destination-marker' : 'map-marker participant-marker';
                node.textContent = item.role === 'Destination' ? 'D' : String(index + 1);
                node.setAttribute('aria-label', `${item.role}: ${item.label}`);
                const marker = new mapboxgl.Marker({ element: node, anchor: 'bottom' })
                    .setLngLat([item.place.longitude, item.place.latitude])
                    .setPopup(new mapboxgl.Popup({ offset: 18 }).setText(`${item.role}: ${item.label}`))
                    .addTo(currentMap);
                return marker;
            });
        if (markerLocations.length === 1) currentMap.easeTo({ center: [markerLocations[0].place.longitude, markerLocations[0].place.latitude], zoom: 13 });
        else if (markerLocations.length > 1) {
                const bounds = new mapboxgl.LngLatBounds();
                markerLocations.forEach(item => bounds.extend([item.place.longitude, item.place.latitude]));
                currentMap.fitBounds(bounds, { padding: 42, maxZoom: 14, duration: 0 });
        }
    }, [markerPayload, mapReady]);

    useEffect(() => {
        const currentMap = map.current;
        if (!currentMap || !mapReady || !selecting || !onPinSelect) return;
        currentMap.getCanvas().style.cursor = 'crosshair';
        const choose = (event: MapMouseEvent) => onPinSelect({ longitude: event.lngLat.lng, latitude: event.lngLat.lat });
        currentMap.on('click', choose);
        return () => { currentMap.off('click', choose); currentMap.getCanvas().style.cursor = ''; };
    }, [mapReady, selecting, onPinSelect]);

    return <aside className="map-panel" aria-label="Trip location map">
        <div className="map-heading"><div><strong>Trip locations</strong><span>Distinct pins identify selected origins and the shared destination.</span></div><details className="map-legend"><summary>Legend</summary><p><span className="legend-pin destination-pin">D</span> Destination</p><p><span className="legend-pin participant-pin">1</span> Participant origins</p></details></div>
        {!token?.trim() ? <div className="map-placeholder"><strong>Map preview setup needed</strong><span>Add a restricted local Mapbox public token to enable the map. Address search remains unavailable until permanent storage is eligible and configured.</span></div> : error ? <div className="map-placeholder" role="status"><strong>Mapbox map is unavailable</strong><span>Check local network access, token restrictions, and map style access.</span></div> : <div ref={mapNode} className="mapbox-map" aria-label={`${resolved.length} selected trip locations`} />}
        {selecting && <p className="map-pick-hint" role="status">Click the map to place the {selecting.role.toLowerCase()}: {selecting.label}</p>}
        <div className="map-attribution">© Mapbox © OpenStreetMap</div>
    </aside>;
}
