/** The route counts as one line; a runtime with routes by channel says how many of them go by channel. */
export function routeCounts(routes: {
    global_override_count: number;
    campaign_route_count: number;
    channel_route_count?: number;
}): string {
    const line = `${routes.global_override_count} global · ${routes.campaign_route_count} per campaign`;
    return routes.channel_route_count ? `${line} · ${routes.channel_route_count} of them by channel` : line;
}
