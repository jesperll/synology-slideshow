import axios from 'axios';
import React, { useEffect, useRef, useState } from 'react';
import { useAdminConnection } from '../hooks/useAdminConnection';
import { createChannel, deleteChannel, getAlbums, getAlbumSlides, getChannelStats, refreshLibrary } from '../services/api';
import { Album, ChannelStats, Slide } from '../types';

export function AdminPage() {
  const {
    snapshot,
    requestNext,
    requestPrevious,
    requestJump,
    requestTogglePause,
    requestSwitchAlbum,
    requestLink,
    requestUnlink
  } = useAdminConnection();
  const [albums, setAlbums] = useState<Album[]>([]);
  const [newChannelName, setNewChannelName] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [slidesByAlbum, setSlidesByAlbum] = useState<Record<number, Slide[]>>({});
  const [expandedChannelId, setExpandedChannelId] = useState<number | null>(null);
  // Albums whose slides have been requested (loaded or in flight), so the eager
  // current-slide loading and the jump grid never fetch the same album twice.
  const requestedAlbumsRef = useRef(new Set<number>());
  const [selectedForLink, setSelectedForLink] = useState<Set<number>>(new Set());
  const [linkError, setLinkError] = useState<string | null>(null);
  const [statsByChannel, setStatsByChannel] = useState<Record<number, ChannelStats>>({});
  const [statsExpandedId, setStatsExpandedId] = useState<number | null>(null);
  const [deleteConfirmId, setDeleteConfirmId] = useState<number | null>(null);
  const [deleteConfirmText, setDeleteConfirmText] = useState('');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [refreshedJustNow, setRefreshedJustNow] = useState(false);
  // Points at whichever slide-grid button is currently marked "selected", so the grid can
  // scroll to it as soon as it opens instead of leaving the admin to hunt for it.
  const selectedSlideRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (expandedChannelId !== null) {
      selectedSlideRef.current?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }, [expandedChannelId]);

  useEffect(() => {
    getAlbums().then((response) => setAlbums(response.data));
  }, []);

  const albumName = (albumId: number | null) => albums.find((a) => a.id === albumId)?.name ?? '(no album)';
  const channelName = (channelId: number) => snapshot?.channels.find((c) => c.channelId === channelId)?.name ?? `#${channelId}`;

  const handleCreate = async (event: React.FormEvent) => {
    event.preventDefault();
    setCreateError(null);
    const name = newChannelName.trim();
    try {
      await createChannel(name);
      setNewChannelName('');
    } catch (err) {
      const serverMessage =
        axios.isAxiosError(err) && typeof err.response?.data === 'string' ? err.response.data : undefined;
      setCreateError(serverMessage ?? `Could not create '${name}' — the name may already be in use or reserved.`);
    }
  };

  const loadSlidesFor = async (albumId: number) => {
    if (requestedAlbumsRef.current.has(albumId)) return;
    requestedAlbumsRef.current.add(albumId);
    try {
      const response = await getAlbumSlides(albumId);
      setSlidesByAlbum((current) => ({ ...current, [albumId]: response.data }));
    } catch (error) {
      requestedAlbumsRef.current.delete(albumId); // allow a later retry
      throw error;
    }
  };

  // Eagerly load slides for every channel's current album so the Current Slide column can
  // show a thumbnail without expanding the jump grid. Keyed on the distinct album ids so
  // it only re-runs when a channel switches album (not on every slide advance).
  const currentAlbumKey = snapshot
    ? Array.from(new Set(snapshot.channels.map((c) => c.currentAlbumId).filter((id): id is number => id != null)))
        .sort((a, b) => a - b)
        .join(',')
    : '';
  useEffect(() => {
    if (currentAlbumKey === '') return;
    for (const albumId of currentAlbumKey.split(',').map(Number)) {
      loadSlidesFor(albumId).catch((error) => console.warn('Failed to load slides for album', albumId, error));
    }
    // loadSlidesFor dedupes via requestedAlbumsRef, so it's safe to omit from the deps.
  }, [currentAlbumKey]);

  const currentSlide = (albumId: number | null, slideId: number | null) =>
    albumId == null || slideId == null ? undefined : slidesByAlbum[albumId]?.find((slide) => slide.id === slideId);

  const toggleExpanded = async (channelId: number, albumId: number | null) => {
    if (expandedChannelId === channelId) {
      setExpandedChannelId(null);
      return;
    }
    if (albumId != null) {
      await loadSlidesFor(albumId);
    }
    setExpandedChannelId(channelId);
  };

  const toggleStats = async (channelId: number) => {
    if (statsExpandedId === channelId) {
      setStatsExpandedId(null);
      return;
    }
    if (!statsByChannel[channelId]) {
      try {
        const response = await getChannelStats(channelId);
        setStatsByChannel((current) => ({ ...current, [channelId]: response.data }));
      } catch (error) {
        console.warn('Failed to load stats for channel', channelId, error);
        return;
      }
    }
    setStatsExpandedId(channelId);
  };

  const toggleSelectedForLink = (channelId: number) => {
    setSelectedForLink((current) => {
      const next = new Set(current);
      if (next.has(channelId)) {
        next.delete(channelId);
      } else {
        next.add(channelId);
      }
      return next;
    });
  };

  const handleLink = async () => {
    setLinkError(null);
    const result = await requestLink(Array.from(selectedForLink));
    if (!result.success) {
      setLinkError(result.error ?? 'Could not link the selected channels.');
      return;
    }
    setSelectedForLink(new Set());
  };

  const cancelDelete = () => {
    setDeleteConfirmId(null);
    setDeleteConfirmText('');
  };

  const confirmDelete = async (channelId: number) => {
    await deleteChannel(channelId);
    cancelDelete();
  };

  const handleRefresh = async () => {
    setIsRefreshing(true);
    setRefreshError(null);
    setRefreshedJustNow(false);
    try {
      await refreshLibrary();
      setRefreshedJustNow(true);
    } catch {
      setRefreshError('Could not refresh the library.');
    } finally {
      setIsRefreshing(false);
    }
  };

  if (!snapshot) {
    return <p>Loading…</p>;
  }

  return (
    <div className="admin-page">
      <h1>Channels</h1>

      <div className="admin-refresh-bar">
        <button onClick={handleRefresh} disabled={isRefreshing}>
          {isRefreshing ? 'Refreshing…' : 'Refresh library'}
        </button>
        {refreshedJustNow && <span className="admin-refresh-success">Library refreshed.</span>}
        {refreshError && <span className="admin-error">{refreshError}</span>}
      </div>

      <form onSubmit={handleCreate}>
        <input
          value={newChannelName}
          onChange={(e) => setNewChannelName(e.target.value)}
          placeholder="New channel name"
        />
        <button type="submit" disabled={newChannelName.trim().length === 0}>
          Create
        </button>
        {createError && <span className="admin-error">{createError}</span>}
      </form>

      <div className="admin-link-bar">
        <button onClick={handleLink} disabled={selectedForLink.size < 2}>
          Link selected
        </button>
        {linkError && <span className="admin-error">{linkError}</span>}
      </div>

      <table className="admin-channel-table">
        <thead>
          <tr>
            <th></th>
            <th>Name</th>
            <th>Viewers</th>
            <th>Status</th>
            <th>Album</th>
            <th>Current Slide</th>
            <th>Linked with</th>
            <th>Controls</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {snapshot.channels.map((channel) => (
            <React.Fragment key={channel.channelId}>
              <tr>
                <td>
                  <input
                    type="checkbox"
                    checked={selectedForLink.has(channel.channelId)}
                    onChange={() => toggleSelectedForLink(channel.channelId)}
                  />
                </td>
                <td>
                  {channel.name}
                  {channel.isDefault && ' (default)'}
                </td>
                <td>{channel.viewerCount}</td>
                <td>{channel.isPaused ? 'Paused' : 'Playing'}</td>
                <td>
                  <select
                    value={channel.currentAlbumId ?? ''}
                    onChange={(e) => requestSwitchAlbum(channel.channelId, Number(e.target.value))}
                  >
                    <option value="" disabled>
                      {albumName(channel.currentAlbumId)}
                    </option>
                    {albums.map((album) => (
                      <option key={album.id} value={album.id}>
                        {album.name}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  {(() => {
                    const slide = currentSlide(channel.currentAlbumId, channel.currentSlideId);
                    return slide ? (
                      <img
                        className="admin-current-slide"
                        src={slide.thumbnailUri}
                        alt={`Current slide of ${channel.name}`}
                        style={{ width: 80, height: 60, objectFit: 'cover' }}
                      />
                    ) : null;
                  })()}
                </td>
                <td>
                  {channel.linkedChannelIds.length === 0 ? (
                    '—'
                  ) : (
                    <>
                      {channel.linkedChannelIds.map(channelName).join(', ')}{' '}
                      <button onClick={() => requestUnlink(channel.channelId)}>Unlink</button>
                    </>
                  )}
                </td>
                <td>
                  <button onClick={() => requestTogglePause(channel.channelId)}>
                    {channel.isPaused ? 'Play' : 'Pause'}
                  </button>
                  <button onClick={() => requestPrevious(channel.channelId)}>Previous</button>
                  <button onClick={() => requestNext(channel.channelId)}>Next</button>
                  <button onClick={() => toggleExpanded(channel.channelId, channel.currentAlbumId)}>
                    {expandedChannelId === channel.channelId ? 'Hide slides' : 'Jump to slide…'}
                  </button>
                  <button onClick={() => toggleStats(channel.channelId)}>
                    {statsExpandedId === channel.channelId ? 'Hide stats' : 'Stats'}
                  </button>
                </td>
                <td>
                  {!channel.isDefault &&
                    (deleteConfirmId === channel.channelId ? (
                      <div className="admin-delete-confirm">
                        <input
                          autoFocus
                          value={deleteConfirmText}
                          onChange={(e) => setDeleteConfirmText(e.target.value)}
                          placeholder={`Type "${channel.name}" to confirm`}
                        />
                        <button
                          className="admin-delete-button"
                          onClick={() => confirmDelete(channel.channelId)}
                          disabled={deleteConfirmText !== channel.name}
                        >
                          Confirm
                        </button>
                        <button onClick={cancelDelete}>Cancel</button>
                      </div>
                    ) : (
                      <button
                        className="admin-delete-button"
                        onClick={() => {
                          setDeleteConfirmId(channel.channelId);
                          setDeleteConfirmText('');
                        }}
                      >
                        Delete
                      </button>
                    ))}
                </td>
              </tr>
              {expandedChannelId === channel.channelId && channel.currentAlbumId != null && (
                <tr>
                  <td colSpan={9}>
                    <ul className="admin-slide-grid">
                      {(slidesByAlbum[channel.currentAlbumId] ?? []).map((slide) => {
                        const isSelected = slide.id === channel.currentSlideId;
                        return (
                          <li key={slide.id}>
                            <button
                              ref={isSelected ? selectedSlideRef : undefined}
                              className={isSelected ? 'selected' : ''}
                              onClick={() => requestJump(channel.channelId, slide.id)}
                            >
                              <img src={slide.thumbnailUri} loading="lazy" alt="" />
                            </button>
                          </li>
                        );
                      })}
                    </ul>
                  </td>
                </tr>
              )}
              {statsExpandedId === channel.channelId && statsByChannel[channel.channelId] && (
                <tr>
                  <td colSpan={9}>
                    <div className="admin-stats">
                      <p>Total views: {statsByChannel[channel.channelId].totalViews}</p>
                      <div>
                        <strong>Most viewed:</strong>
                        <ul>
                          {statsByChannel[channel.channelId].topViewed.map((s) => (
                            <li key={s.slideId}>
                              Slide #{s.slideId} — {s.viewCount} views
                            </li>
                          ))}
                        </ul>
                      </div>
                      <div>
                        <strong>Least viewed:</strong>
                        <ul>
                          {statsByChannel[channel.channelId].leastViewed.map((s) => (
                            <li key={s.slideId}>
                              Slide #{s.slideId} — {s.viewCount} views
                            </li>
                          ))}
                        </ul>
                      </div>
                    </div>
                  </td>
                </tr>
              )}
            </React.Fragment>
          ))}
        </tbody>
      </table>
    </div>
  );
}
