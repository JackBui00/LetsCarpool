export type PlaceSource = 'demo' | 'mapbox-searchbox-temporary' | 'mapbox-geocoding-permanent' | 'user-provided' | 'unresolved';
export type LocationProvenance = 'userProvided' | 'mapboxSearchBoxTemporary' | 'mapboxGeocodingPermanent';
export type DurableLocationProvenance = Exclude<LocationProvenance, 'mapboxSearchBoxTemporary'>;

export type PlaceSelection = {
    label: string;
    latitude: number | null;
    longitude: number | null;
    placeId: string | null;
    source: PlaceSource;
};

export function demoPlace(label: string, latitude: number, longitude: number): PlaceSelection {
    return { label, latitude, longitude, placeId: null, source: 'demo' };
}

export function unresolvedPlace(label: string): PlaceSelection {
    return { label, latitude: null, longitude: null, placeId: null, source: 'unresolved' };
}

export function editPlaceLabel(label: string): PlaceSelection {
    return unresolvedPlace(label);
}

export function isPlaceResolved(place: PlaceSelection) {
    return place.source !== 'unresolved'
        && place.latitude !== null
        && place.longitude !== null
        && Number.isFinite(place.latitude)
        && Number.isFinite(place.longitude);
}

export function hasMapboxToken(token: string | undefined) {
    return Boolean(token?.trim());
}

export function isPlanReady(token: string | undefined, places: PlaceSelection[]) {
    return hasMapboxToken(token) && places.length > 0 && places.every(place =>
        isPlaceResolved(place) && (place.source === 'user-provided' || place.source === 'mapbox-geocoding-permanent'));
}

export function provenance(place: PlaceSelection): DurableLocationProvenance | null {
    if (place.source === 'user-provided') return 'userProvided';
    if (place.source === 'mapbox-geocoding-permanent') return 'mapboxGeocodingPermanent';
    return null;
}

export type MapboxSuggestion = { mapbox_id: string; name: string; full_address?: string; place_formatted: string };
export type MapboxResolvedLocation = { label: string; latitude: number; longitude: number; mapboxId: string };

export class MapboxSearchSequence {
    private revision = 0;
    begin() { return ++this.revision; }
    invalidate() { this.revision += 1; }
    isCurrent(revision: number) { return this.revision === revision; }
}

export class MapboxSearchSession {
    private token = '';
    private readonly createToken: () => string;
    constructor(createToken: () => string = () => crypto.randomUUID()) { this.createToken = createToken; }
    current() { this.token ||= this.createToken(); return this.token; }
    complete() { this.token = ''; }
}

export class MapboxProviderError extends Error {
    readonly status: number;
    constructor(status: number) { super('Map provider request failed.'); this.status = status; }
}

export function mapboxSuggestUrl(token: string, query: string, sessionToken: string) {
    const url = new URL('https://api.mapbox.com/search/searchbox/v1/suggest');
    url.searchParams.set('q', query);
    url.searchParams.set('session_token', sessionToken);
    url.searchParams.set('limit', '5');
    url.searchParams.set('language', 'en');
    url.searchParams.set('access_token', token);
    return url.toString();
}

export function mapboxRetrieveUrl(mapboxId: string, token: string, sessionToken: string) {
    const url = new URL(`https://api.mapbox.com/search/searchbox/v1/retrieve/${encodeURIComponent(mapboxId)}`);
    url.searchParams.set('session_token', sessionToken);
    url.searchParams.set('access_token', token);
    return url.toString();
}

export function parseMapboxSuggestions(value: unknown): MapboxSuggestion[] {
    if (!isRecord(value) || !Array.isArray(value.suggestions)) return [];
    return value.suggestions.filter(isRecord)
        .filter((item): item is MapboxSuggestion => typeof item.mapbox_id === 'string' && typeof item.name === 'string' && typeof item.place_formatted === 'string')
        .slice(0, 5);
}

export function parseMapboxRetrievedLocation(value: unknown): MapboxResolvedLocation | null {
    if (!isRecord(value) || !Array.isArray(value.features)) return null;
    const feature = value.features.find(isRecord);
    if (!feature || !isRecord(feature.geometry) || !Array.isArray(feature.geometry.coordinates) || !isRecord(feature.properties)) return null;
    const [longitude, latitude] = feature.geometry.coordinates;
    const label = feature.properties.full_address ?? feature.properties.name_preferred ?? feature.properties.name;
    if (typeof longitude !== 'number' || !Number.isFinite(longitude) || Math.abs(longitude) > 180 || typeof latitude !== 'number' || !Number.isFinite(latitude) || Math.abs(latitude) > 90 || typeof label !== 'string' || !label.trim()) return null;
    return { label, longitude, latitude, mapboxId: typeof feature.properties.mapbox_id === 'string' ? feature.properties.mapbox_id : '' };
}

export async function requestMapboxSuggestions(token: string, query: string, sessionToken: string, signal: AbortSignal, fetcher: typeof fetch = fetch) {
    const response = await fetcher(mapboxSuggestUrl(token, query, sessionToken), { signal });
    if (!response.ok) throw new MapboxProviderError(response.status);
    return parseMapboxSuggestions(await response.json() as unknown);
}

export async function requestMapboxLocation(mapboxId: string, token: string, sessionToken: string, fetcher: typeof fetch = fetch) {
    const response = await fetcher(mapboxRetrieveUrl(mapboxId, token, sessionToken));
    if (!response.ok) throw new MapboxProviderError(response.status);
    return parseMapboxRetrievedLocation(await response.json() as unknown);
}

export function mapboxProviderMessage(status: number) {
    if (status === 401) return 'Map search is not authorized. Check the local token setup and allowed scopes.';
    if (status === 403) return 'Map search is restricted for this local preview. Check token and account access.';
    if (status === 429) return 'Map search reached a provider rate or usage limit. Try again later.';
    return 'Map search is temporarily unavailable. Check the network and try again.';
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
