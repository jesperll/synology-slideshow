export interface Album {
  id: number;
  name: string;
  thumbnail: string;
}

export interface Slide {
  id: number;
  uri: string;
  thumbnailUri: string;
  description: string;
  location: string;
  date: string;
}

export enum SwipeDirection {
  None = 0,
  LeftToRight = 1,
  RightToLeft = 2,
  TopToBottom = 3,
  BottomToTop = 4
}

export enum ImageZoomMode {
  Fit = 'contain',
  Fill = 'cover'
}

export interface AppSettings {
  imageZoomMode: ImageZoomMode;
  showBlurredBackground: boolean;
  kenBurnsEffect: boolean;
  slideshowSpeed: number; // in seconds
}

export interface ChannelState {
  channelId: number;
  name: string;
  currentAlbumId: number | null;
  currentSlideId: number | null;
  isPaused: boolean;
  isDefault: boolean;
}

export interface ChannelSummary {
  id: number;
  name: string;
  isDefault: boolean;
}

export interface AdminChannelEntry {
  channelId: number;
  name: string;
  currentAlbumId: number | null;
  currentSlideId: number | null;
  isPaused: boolean;
  isDefault: boolean;
  viewerCount: number;
  linkedChannelIds: number[];
}

export interface LinkResult {
  success: boolean;
  error: string | null;
}

export interface AdminSnapshot {
  channels: AdminChannelEntry[];
}

export interface SlideViewStat {
  slideId: number;
  viewCount: number;
}

export interface ChannelStats {
  totalViews: number;
  topViewed: SlideViewStat[];
  leastViewed: SlideViewStat[];
}
