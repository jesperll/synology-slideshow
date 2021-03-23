export interface Album {
  id: number;
  name: string;
  thumbnail: string;
}

export interface Slide {
  id: number;
  uri: string;
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
