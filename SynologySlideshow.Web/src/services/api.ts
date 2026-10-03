import axios from 'axios';
import { AdminSnapshot, Album, ChannelStats, ChannelSummary, Slide } from '../types';

const api = axios.create({
  baseURL: '/api'
});

export const getAlbums = () => api.get<Album[]>('/albums');

export const getAlbumSlides = (albumId: number) =>
  api.get<Slide[]>(`/albums/${albumId}/slides`);

export const getChannels = () => api.get<ChannelSummary[]>('/channels');

export const createChannel = (name: string) => api.post<ChannelSummary>('/channels', { name });

export const deleteChannel = (id: number) => api.delete(`/channels/${id}`);

export const getAdminSnapshot = () => api.get<AdminSnapshot>('/admin/snapshot');

export const getChannelStats = (channelId: number) => api.get<ChannelStats>(`/channels/${channelId}/stats`);

export const refreshLibrary = () => api.post('/admin/refresh');
