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
      { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 3, linkedChannelIds: [] }
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
      requestLink: vi.fn().mockResolvedValue({ success: true, error: null }),
      requestUnlink: vi.fn(),
      ...overrides
    });

  beforeEach(() => {
    vi.mocked(api.getAlbums).mockResolvedValue({ data: [{ id: 5, name: 'Holiday', thumbnail: '' }] } as any);
    vi.mocked(api.createChannel).mockResolvedValue({ data: { id: 2, name: 'bedroom' } } as any);
    vi.mocked(api.deleteChannel).mockResolvedValue({} as any);
    vi.mocked(api.getAlbumSlides).mockResolvedValue({
      data: [
        { id: 10, uri: '/api/albums/5/slides/10.jpg', description: '', location: '', date: '' },
        { id: 11, uri: '/api/albums/5/slides/11.jpg', description: '', location: '', date: '' }
      ]
    } as any);
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

  it("shows a thumbnail of each channel's current slide once its album's slides have loaded", async () => {
    mockHook();

    render(<AdminPage />);

    const thumbnail = await screen.findByAltText('Current slide of kitchen');
    expect(thumbnail).toHaveAttribute('src', '/api/albums/5/slides/10.jpg');
    expect(api.getAlbumSlides).toHaveBeenCalledWith(5);
  });

  it('shows no current-slide thumbnail when the channel has no current slide', async () => {
    mockHook({
      snapshot: {
        anonymousCount: 0,
        channels: [{ channelId: 1, name: 'kitchen', currentAlbumId: null, currentSlideId: null, isPaused: true, viewerCount: 0, linkedChannelIds: [] }]
      }
    });
    vi.mocked(api.getAlbumSlides).mockClear();

    render(<AdminPage />);

    await waitFor(() => expect(api.getAlbums).toHaveBeenCalled());
    expect(screen.queryByAltText('Current slide of kitchen')).not.toBeInTheDocument();
    expect(api.getAlbumSlides).not.toHaveBeenCalled();
  });

  const twoChannelSnapshot: AdminSnapshot = {
    anonymousCount: 2,
    channels: [
      { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 3, linkedChannelIds: [] },
      { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [] }
    ]
  };

  it('links the selected channels once two or more are checked', async () => {
    const requestLink = vi.fn().mockResolvedValue({ success: true, error: null });
    mockHook({ snapshot: twoChannelSnapshot, requestLink });

    render(<AdminPage />);

    const checkboxes = screen.getAllByRole('checkbox');
    fireEvent.click(checkboxes[0]);
    fireEvent.click(checkboxes[1]);
    fireEvent.click(screen.getByRole('button', { name: 'Link selected' }));

    await waitFor(() => expect(requestLink).toHaveBeenCalledWith([1, 2]));
  });

  it('disables the link button until at least two channels are selected', () => {
    mockHook({ snapshot: twoChannelSnapshot });

    render(<AdminPage />);

    expect(screen.getByRole('button', { name: 'Link selected' })).toBeDisabled();

    fireEvent.click(screen.getAllByRole('checkbox')[0]);

    expect(screen.getByRole('button', { name: 'Link selected' })).toBeDisabled();
  });

  it('shows a link error returned by the server instead of clearing the selection', async () => {
    const requestLink = vi.fn().mockResolvedValue({ success: false, error: 'Channel 2 is already in a sync group.' });
    mockHook({ snapshot: twoChannelSnapshot, requestLink });

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('checkbox')[0]);
    fireEvent.click(screen.getAllByRole('checkbox')[1]);
    fireEvent.click(screen.getByRole('button', { name: 'Link selected' }));

    expect(await screen.findByText('Channel 2 is already in a sync group.')).toBeInTheDocument();
    expect(screen.getAllByRole('checkbox')[0]).toBeChecked();
    expect(screen.getAllByRole('checkbox')[1]).toBeChecked();
  });

  it("shows a linked channel's name (not just its id) in the Linked with column", () => {
    const linkedSnapshot: AdminSnapshot = {
      anonymousCount: 0,
      channels: [
        { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [2] },
        { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [1] }
      ]
    };
    mockHook({ snapshot: linkedSnapshot });

    const { container } = render(<AdminPage />);

    const rows = container.querySelectorAll('tbody > tr');
    const kitchenLinkedCell = rows[0].querySelectorAll('td')[6];
    const livingRoomLinkedCell = rows[1].querySelectorAll('td')[6];

    expect(kitchenLinkedCell.textContent).toContain('living-room');
    expect(kitchenLinkedCell.textContent).not.toContain('#2');
    expect(livingRoomLinkedCell.textContent).toContain('kitchen');
    expect(livingRoomLinkedCell.textContent).not.toContain('#1');
  });

  it('unlinks a channel through its row button', () => {
    const requestUnlink = vi.fn();
    const linkedSnapshot: AdminSnapshot = {
      anonymousCount: 0,
      channels: [
        { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [2] },
        { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, viewerCount: 1, linkedChannelIds: [1] }
      ]
    };
    mockHook({ snapshot: linkedSnapshot, requestUnlink });

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Unlink' })[0]);

    expect(requestUnlink).toHaveBeenCalledWith(1);
  });
});
