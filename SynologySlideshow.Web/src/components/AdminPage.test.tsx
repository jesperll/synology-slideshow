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
    channels: [
      { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false, viewerCount: 3, linkedChannelIds: [] }
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
    vi.mocked(api.deleteChannel).mockReset().mockResolvedValue({} as any);
    vi.mocked(api.refreshLibrary).mockReset().mockResolvedValue({} as any);
    vi.mocked(api.getAlbumSlides).mockResolvedValue({
      data: [
        { id: 10, uri: '/api/albums/5/slides/10.jpg', thumbnailUri: '/api/albums/5/slides/10/thumbnail.jpg', description: '', location: '', date: '' },
        { id: 11, uri: '/api/albums/5/slides/11.jpg', thumbnailUri: '/api/albums/5/slides/11/thumbnail.jpg', description: '', location: '', date: '' }
      ]
    } as any);
  });

  it('renders each channel from the snapshot', () => {
    mockHook();

    render(<AdminPage />);

    expect(screen.getByText('kitchen')).toBeInTheDocument();
    expect(screen.getByText('Playing')).toBeInTheDocument();
  });

  it("labels the default channel and hides its Delete button", () => {
    mockHook({
      snapshot: {
        channels: [
          { channelId: 1, name: 'Default', currentAlbumId: null, currentSlideId: null, isPaused: true, isDefault: true, viewerCount: 0, linkedChannelIds: [] }
        ]
      }
    });

    render(<AdminPage />);

    expect(screen.getByText((_, element) => element?.textContent === 'Default (default)')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
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

  it('deletes a channel only after typing its exact name to confirm', async () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(api.deleteChannel).not.toHaveBeenCalled();

    const confirmButton = screen.getByRole('button', { name: 'Confirm' });
    expect(confirmButton).toBeDisabled();

    fireEvent.change(screen.getByPlaceholderText('Type "kitchen" to confirm'), { target: { value: 'kitche' } });
    expect(confirmButton).toBeDisabled();

    fireEvent.change(screen.getByPlaceholderText('Type "kitchen" to confirm'), { target: { value: 'kitchen' } });
    expect(confirmButton).toBeEnabled();

    fireEvent.click(confirmButton);

    await waitFor(() => expect(api.deleteChannel).toHaveBeenCalledWith(1));
  });

  it('cancels the delete confirmation without deleting', () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
    fireEvent.change(screen.getByPlaceholderText('Type "kitchen" to confirm'), { target: { value: 'kitchen' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(api.deleteChannel).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument();
  });

  it('refreshes the library and shows a success message', async () => {
    mockHook();

    render(<AdminPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Refresh library' }));

    await waitFor(() => expect(api.refreshLibrary).toHaveBeenCalled());
    expect(await screen.findByText('Library refreshed.')).toBeInTheDocument();
  });

  it('disables the refresh button while the request is in flight', async () => {
    mockHook();
    let resolveRefresh!: () => void;
    vi.mocked(api.refreshLibrary).mockReturnValue(
      new Promise((resolve) => {
        resolveRefresh = () => resolve({} as any);
      })
    );

    render(<AdminPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Refresh library' }));

    expect(screen.getByRole('button', { name: 'Refreshing…' })).toBeDisabled();

    resolveRefresh();
    await screen.findByRole('button', { name: 'Refresh library' });
  });

  it('shows an error message when the refresh request fails', async () => {
    mockHook();
    vi.mocked(api.refreshLibrary).mockRejectedValue(new Error('network down'));

    render(<AdminPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Refresh library' }));

    expect(await screen.findByText('Could not refresh the library.')).toBeInTheDocument();
    expect(screen.queryByText('Library refreshed.')).not.toBeInTheDocument();
  });

  it('removes the row once the live snapshot no longer includes the channel', () => {
    mockHook();

    const { rerender } = render(<AdminPage />);

    expect(screen.getByText('kitchen')).toBeInTheDocument();

    mockHook({ snapshot: { channels: [] } });
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
    expect(thumbnail).toHaveAttribute('src', '/api/albums/5/slides/10/thumbnail.jpg');
    expect(api.getAlbumSlides).toHaveBeenCalledWith(5);
  });

  it('shows no current-slide thumbnail when the channel has no current slide', async () => {
    mockHook({
      snapshot: {
        channels: [{ channelId: 1, name: 'kitchen', currentAlbumId: null, currentSlideId: null, isPaused: true, isDefault: false, viewerCount: 0, linkedChannelIds: [] }]
      }
    });
    vi.mocked(api.getAlbumSlides).mockClear();

    render(<AdminPage />);

    await waitFor(() => expect(api.getAlbums).toHaveBeenCalled());
    expect(screen.queryByAltText('Current slide of kitchen')).not.toBeInTheDocument();
    expect(api.getAlbumSlides).not.toHaveBeenCalled();
  });

  const twoChannelSnapshot: AdminSnapshot = {
    channels: [
      { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false, viewerCount: 3, linkedChannelIds: [] },
      { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false, viewerCount: 1, linkedChannelIds: [] }
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
      channels: [
        { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false, viewerCount: 1, linkedChannelIds: [2] },
        { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false, viewerCount: 1, linkedChannelIds: [1] }
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
      channels: [
        { channelId: 1, name: 'kitchen', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false, viewerCount: 1, linkedChannelIds: [2] },
        { channelId: 2, name: 'living-room', currentAlbumId: 5, currentSlideId: 10, isPaused: false, isDefault: false, viewerCount: 1, linkedChannelIds: [1] }
      ]
    };
    mockHook({ snapshot: linkedSnapshot, requestUnlink });

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Unlink' })[0]);

    expect(requestUnlink).toHaveBeenCalledWith(1);
  });

  it('shows total views and top/least viewed slides when Stats is expanded', async () => {
    mockHook();
    vi.mocked(api.getChannelStats).mockResolvedValue({
      data: { totalViews: 5, topViewed: [{ slideId: 10, viewCount: 3 }], leastViewed: [{ slideId: 20, viewCount: 2 }] }
    } as any);

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Stats' })[0]);

    expect(await screen.findByText('Total views: 5')).toBeInTheDocument();
    expect(screen.getByText('Slide #10 — 3 views')).toBeInTheDocument();
    expect(screen.getByText('Slide #20 — 2 views')).toBeInTheDocument();
  });

  it('does not crash or expand the stats panel when getChannelStats rejects', async () => {
    mockHook();
    vi.mocked(api.getChannelStats).mockRejectedValue(new Error('network error'));

    render(<AdminPage />);

    fireEvent.click(screen.getAllByRole('button', { name: 'Stats' })[0]);

    await waitFor(() => expect(api.getChannelStats).toHaveBeenCalled());

    expect(screen.queryByText(/Total views:/)).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Stats' })[0]).toBeInTheDocument();
  });
});
