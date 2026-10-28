export function isValidTimeZone(timeZoneId: string) {
    try { new Intl.DateTimeFormat('en-US', { timeZone: timeZoneId }).format(); return true; }
    catch { return false; }
}

export function isValidLocalTimeInZone(localValue: string, timeZoneId: string) {
    try { isoWithZoneOffset(localValue, timeZoneId); return true; }
    catch { return false; }
}

export function localInputInZone(isoValue: string, timeZoneId: string) {
    const date = new Date(isoValue);
    const parts = new Intl.DateTimeFormat('en-US', {
        timeZone: timeZoneId, year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    }).formatToParts(date);
    const values = Object.fromEntries(parts.map(part => [part.type, part.value]));
    return `${values.year}-${values.month}-${values.day}T${values.hour}:${values.minute}`;
}

export function isoWithZoneOffset(localValue: string, timeZoneId: string) {
    const [datePart, timePart] = localValue.split('T');
    if (!datePart || !timePart || !isValidTimeZone(timeZoneId)) throw new Error('Enter a valid arrival time and IANA timezone.');
    const [year, month, day] = datePart.split('-').map(Number);
    const [hour, minute] = timePart.split(':').map(Number);
    const wallClockMs = Date.UTC(year, month - 1, day, hour, minute);
    let offsetMinutes = offsetAt(new Date(wallClockMs), timeZoneId);
    let instant = new Date(wallClockMs - offsetMinutes * 60_000);
    const actualOffset = offsetAt(instant, timeZoneId);
    if (actualOffset !== offsetMinutes) {
        offsetMinutes = actualOffset;
        instant = new Date(wallClockMs - offsetMinutes * 60_000);
    }
    if (localInputInZone(instant.toISOString(), timeZoneId) !== localValue.slice(0, 16)) {
        throw new Error('That local arrival time does not exist in this timezone because the clock changes. Choose another time.');
    }
    const sign = offsetMinutes >= 0 ? '+' : '-';
    const hours = String(Math.floor(Math.abs(offsetMinutes) / 60)).padStart(2, '0');
    const minutes = String(Math.abs(offsetMinutes) % 60).padStart(2, '0');
    const seconds = timePart.split(':')[2] ?? '00';
    return `${datePart}T${timePart.slice(0, 5)}:${seconds}${sign}${hours}:${minutes}`;
}

function offsetAt(date: Date, timeZoneId: string) {
    const zoneName = new Intl.DateTimeFormat('en-US', { timeZone: timeZoneId, timeZoneName: 'longOffset' })
        .formatToParts(date).find(part => part.type === 'timeZoneName')?.value;
    const match = zoneName?.match(/GMT(?:([+-])(\d{2}):?(\d{2}))?/);
    if (!match) throw new Error('This browser could not resolve the selected timezone offset.');
    if (!match[1]) return 0;
    const value = Number(match[2]) * 60 + Number(match[3]);
    return match[1] === '+' ? value : -value;
}
