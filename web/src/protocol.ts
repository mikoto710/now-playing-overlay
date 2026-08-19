export const protocolVersion = 3 as const;

export type PlaybackState = "playing" | "paused" | "stopped" | "idle" | "unavailable";

export type MediaPlaybackKind = "unknown" | "music" | "video" | "image";

export interface TrackDto {
  title: string;
  artist: string;
  albumTitle: string | null;
  albumArtist: string | null;
  subtitle: string | null;
  trackNumber: number | null;
  albumTrackCount: number | null;
  playbackType: MediaPlaybackKind | null;
  genres: string[];
}

export interface ArtworkDto {
  artworkRevision: number;
  artworkId: string;
  url: string;
}

export interface PlaybackTimelineDto {
  positionMs: number;
  durationMs: number;
  sampledAt: string;
}

export type SourceProvider = string;

export interface SourceDto {
  provider: SourceProvider;
}

export interface NowPlayingStateDto {
  protocolVersion: typeof protocolVersion;
  serverInstanceId: string;
  snapshotRevision: number;
  source: SourceDto | null;
  playback: PlaybackState;
  track: TrackDto | null;
  timeline: PlaybackTimelineDto | null;
  artwork: ArtworkDto | null;
  observedAt: string;
}

const playbackStates = new Set<PlaybackState>([
  "playing",
  "paused",
  "stopped",
  "idle",
  "unavailable",
]);
const playbackKinds = new Set<MediaPlaybackKind>(["unknown", "music", "video", "image"]);
export const sourceProviderMaximumLength = 64;
const sourceProviderPattern = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const artworkIdPattern = /^[0-9a-f]{64}$/;
const utcTimestampPattern =
  /^\d{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01])T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/;

export function parseNowPlayingState(value: unknown): NowPlayingStateDto {
  if (!isRecord(value) || value.protocolVersion !== protocolVersion) {
    throw new Error("Unsupported now-playing protocol version.");
  }

  if (
    typeof value.serverInstanceId !== "string" ||
    !uuidPattern.test(value.serverInstanceId) ||
    !isNonNegativeInteger(value.snapshotRevision) ||
    !isSource(value.source) ||
    !playbackStates.has(value.playback as PlaybackState) ||
    !isTrack(value.track) ||
    !isTimeline(value.timeline) ||
    !isArtwork(value.artwork) ||
    typeof value.observedAt !== "string" ||
    !Number.isFinite(Date.parse(value.observedAt))
  ) {
    throw new Error("Invalid now-playing protocol payload.");
  }

  const state = value as unknown as NowPlayingStateDto;
  // Mirror the host state matrix so malformed payloads never reach the widget.
  if (!hasValidStateMatrix(state)) {
    throw new Error("Invalid now-playing state combination.");
  }

  return state;
}

function hasValidStateMatrix(state: NowPlayingStateDto): boolean {
  if (state.artwork !== null && state.track === null) {
    return false;
  }

  switch (state.playback) {
    case "playing":
      return state.source !== null && state.track !== null;
    case "paused":
      return state.source !== null;
    case "stopped":
      return state.source !== null && state.timeline === null;
    case "idle":
      return (
        state.source !== null &&
        state.track === null &&
        state.timeline === null &&
        state.artwork === null
      );
    case "unavailable":
      return state.track === null && state.timeline === null && state.artwork === null;
  }
}

function isSource(value: unknown): value is SourceDto | null {
  if (value === null) {
    return true;
  }

  return (
    isRecord(value) &&
    Object.keys(value).length === 1 &&
    typeof value.provider === "string" &&
    value.provider.length <= sourceProviderMaximumLength &&
    sourceProviderPattern.test(value.provider)
  );
}

function isTrack(value: unknown): value is TrackDto | null {
  if (value === null) {
    return true;
  }

  return (
    isRecord(value) &&
    typeof value.title === "string" &&
    typeof value.artist === "string" &&
    isOptionalString(value.albumTitle) &&
    isOptionalString(value.albumArtist) &&
    isOptionalString(value.subtitle) &&
    isOptionalPositiveInteger(value.trackNumber) &&
    isOptionalPositiveInteger(value.albumTrackCount) &&
    (value.playbackType === null || playbackKinds.has(value.playbackType as MediaPlaybackKind)) &&
    Array.isArray(value.genres) &&
    value.genres.every((genre) => typeof genre === "string")
  );
}

function isTimeline(value: unknown): value is PlaybackTimelineDto | null {
  if (value === null) {
    return true;
  }

  return (
    isRecord(value) &&
    hasExactKeys(value, ["positionMs", "durationMs", "sampledAt"]) &&
    isNonNegativeInteger(value.positionMs) &&
    isPositiveInteger(value.durationMs) &&
    value.positionMs <= value.durationMs &&
    isUtcTimestamp(value.sampledAt)
  );
}

function isArtwork(value: unknown): value is ArtworkDto | null {
  if (value === null) {
    return true;
  }

  return (
    isRecord(value) &&
    isPositiveInteger(value.artworkRevision) &&
    typeof value.artworkId === "string" &&
    artworkIdPattern.test(value.artworkId) &&
    value.url === `/api/v3/artwork/${value.artworkId}`
  );
}

function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
  const keys = Object.keys(value);
  return keys.length === expected.length && expected.every((key) => keys.includes(key));
}

function isUtcTimestamp(value: unknown): value is string {
  return (
    typeof value === "string" &&
    utcTimestampPattern.test(value) &&
    Number.isFinite(Date.parse(value))
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isOptionalString(value: unknown): value is string | null {
  return value === null || typeof value === "string";
}

function isNonNegativeInteger(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) >= 0;
}

function isPositiveInteger(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) > 0;
}

function isOptionalPositiveInteger(value: unknown): value is number | null {
  return value === null || isPositiveInteger(value);
}
