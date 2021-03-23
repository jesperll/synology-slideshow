import axios from 'axios';
import { Album, Slide } from '../types';

const api = axios.create({
  baseURL: '/api'
});

export const getAlbums = () => api.get<Album[]>('/albums');

export const getAlbumSlides = (albumId: number) => 
  api.get<Slide[]>(`/albums/${albumId}/slides`);
