export function formatEstimatedMinutes(minutes: number): string {
    return (Math.round((minutes + Number.EPSILON) * 10) / 10).toFixed(1);
}
