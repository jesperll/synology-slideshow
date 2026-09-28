import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AdminPage } from './AdminPage';
import * as adminHook from '../hooks/useAdminConnection';
import * as api from '../services/api';
import { AdminSnapshot } from '../types';

const axiosError = (data: unknown) => ({ isAxiosError: true, response: { data, status: 409 } });

vi.mock('../hooks/useAdminConnection');
vi.mock('../services/api');

describe('AdminPage', () => {
  const baseSnapshot: AdminSnapshot = {
    anonymousCount: 2,
    channels: [
      { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 3 }
    ]
  };

  const mockHook = (overrides: Partial<ReturnType<typeof adminHook.useAdminConnection>> = {}) =>
    vi.mocked(adminHook.useAdminConnection).mockReturnValue({
      snapshot: baseSnapshot,
      requestNext: vi.fn(),
      requestPrevious: vi.fn(),
      requestJump: vi.fn(),
      requestTogglePause: vi.fn(),
      requestSwitchAlbum: vi.fn(),
      ...overrides
    });

  beforeEach(() => {
    vi.mocked(api.getAlbums).mockResolvedValue({ data: [{ id: 5, name: 'Holiday', thumbnail: '' }] } as any);
    vi.mocked(api.createChannel).mockResolvedValue({ data: { id: 2, name: 'bedroom' } } as any);
    vi.mocked(api.deleteChannel).mockResolvedValue({} as any);
  });

  it('renders the anonymous count and each channel from the snapshot', () => {
    mockHook();

    render(<AdminPage />);

    expect(screen.getByText('Anonymous: 2 connected')).toBeInTheDocument();
    expect(screen.getByText('kitchen')).toBeInTheDocument();
    expect(screen.getByText('Playing')).toBeInTheDocument();
  });

  it('creates a channel through the form', async () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.change(screen.getByPlaceholderText('New channel name'), { target: { value: 'bedroom' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() => expect(api.createChannel).toHaveBeenCalledWith('bedroom'));
  });

  it('shows the server-provided error message when channel creation fails', async () => {
    mockHook();
    vi.mocked(api.createChannel).mockRejectedValueOnce(
      axiosError("A channel named 'bedroom' already exists.")
    );

    render(<AdminPage />);

    fireEvent.change(screen.getByPlaceholderText('New channel name'), { target: { value: 'bedroom' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() =>
      expect(screen.getByText("A channel named 'bedroom' already exists.")).toBeInTheDocument()
    );
    expect(screen.queryByText(/may already be in use or reserved/)).not.toBeInTheDocument();
  });

  it('falls back to the generic error message when the failure has no server message', async () => {
    mockHook();
    vi.mocked(api.createChannel).mockRejectedValueOnce(new Error('network down'));

    render(<AdminPage />);

    fireEvent.change(screen.getByPlaceholderText('New channel name'), { target: { value: 'bedroom' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    await waitFor(() =>
      expect(
        screen.getByText("Could not create 'bedroom' — the name may already be in use or reserved.")
      ).toBeInTheDocument()
    );
  });

  it('requests a pause toggle for the right channel', () => {
    const requestTogglePause = vi.fn();
    mockHook({ requestTogglePause });

    render(<AdminPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Pause' }));

    expect(requestTogglePause).toHaveBeenCalledWith(1);
  });

  it('deletes a channel through its row button', async () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(api.deleteChannel).toHaveBeenCalledWith(1));
  });

  it('removes the row once the live snapshot no longer includes the channel', () => {
    mockHook();

    const { rerender } = render(<AdminPage />);

    expect(screen.getByText('kitchen')).toBeInTheDocument();

    mockHook({ snapshot: { anonymousCount: 2, channels: [] } });
    rerender(<AdminPage />);

    expect(screen.queryByText('kitchen')).not.toBeInTheDocument();
  });

  it('shows a loading state before the first snapshot arrives', () => {
    mockHook({ snapshot: null } as any);

    render(<AdminPage />);

    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });
});
