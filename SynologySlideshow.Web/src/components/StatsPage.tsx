import React, { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { getChannelStats } from '../services/api';
import { ChannelStats, SlideViewStat, ViewCountBucket } from '../types';

// A photo shown hundreds of times would otherwise stretch the chart into an unreadable strip.
const MAX_BARS = 25;

interface Bar {
  label: string;
  photoCount: number;
  neverShown: boolean;
}

function toBars(histogram: ViewCountBucket[]): Bar[] {
  const bars: Bar[] = histogram
    .filter((bucket) => bucket.viewCount < MAX_BARS)
    .map((bucket) => ({ label: String(bucket.viewCount), photoCount: bucket.photoCount, neverShown: bucket.viewCount === 0 }));

  const tail = histogram.filter((bucket) => bucket.viewCount >= MAX_BARS);
  if (tail.length > 0) {
    bars.push({ label: `${MAX_BARS}+`, photoCount: tail.reduce((sum, bucket) => sum + bucket.photoCount, 0), neverShown: false });
  }
  return bars;
}

const viewsText = (count: number) => (count === 0 ? 'never shown' : count === 1 ? '1 view' : `${count} views`);

function Histogram({ histogram }: { histogram: ViewCountBucket[] }) {
  const bars = toBars(histogram);
  const tallest = Math.max(1, ...bars.map((bar) => bar.photoCount));

  return (
    <div className="stats-histogram-wrap">
      <div className="stats-histogram" role="list" aria-label="Number of photos by how many times they have been shown">
        {bars.map((bar) => (
          <div
            key={bar.label}
            role="listitem"
            className={bar.neverShown ? 'stats-bar-column never-shown' : 'stats-bar-column'}
            title={`${bar.photoCount} photos shown ${bar.neverShown ? 'never' : `${bar.label} times`}`}
          >
            <span className="stats-bar-value">{bar.photoCount}</span>
            <div className="stats-bar" style={{ height: `${(bar.photoCount / tallest) * 100}%` }} />
            <span className="stats-bar-label">{bar.label}</span>
          </div>
        ))}
      </div>
      <p className="stats-axis-caption">Times shown (x) vs. number of photos (y)</p>
    </div>
  );
}

// The thumbnail is only mounted while a row is hovered, focused or tapped, so the page
// doesn't fetch dozens of images up front.
function PhotoList({ title, items, albumId }: { title: string; items: SlideViewStat[]; albumId: number | null }) {
  const [previewId, setPreviewId] = useState<number | null>(null);

  return (
    <section className="stats-card">
      <h2>{title}</h2>
      <ul className="stats-photo-list">
        {items.map((item) => (
          <li
            key={item.slideId}
            tabIndex={0}
            onMouseEnter={() => setPreviewId(item.slideId)}
            onMouseLeave={() => setPreviewId(null)}
            onFocus={() => setPreviewId(item.slideId)}
            onBlur={() => setPreviewId(null)}
            onClick={() => setPreviewId((current) => (current === item.slideId ? null : item.slideId))}
          >
            <span>Photo #{item.slideId}</span>
            <span className="stats-photo-views">{viewsText(item.viewCount)}</span>
            {previewId === item.slideId && albumId != null && (
              <img
                className="stats-photo-preview"
                src={`/api/albums/${albumId}/slides/${item.slideId}/thumbnail.jpg`}
                alt={`Photo #${item.slideId}`}
              />
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

export function StatsPage() {
  const { channelId } = useParams();
  const [stats, setStats] = useState<ChannelStats | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setStats(null);
    setFailed(false);
    getChannelStats(Number(channelId))
      .then((response) => setStats(response.data))
      .catch(() => setFailed(true));
  }, [channelId]);

  const back = (
    <p>
      <a href="/admin">← All channels</a>
    </p>
  );

  if (failed) {
    return (
      <div className="admin-page">
        {back}
        <p className="admin-error">Could not load stats for this channel.</p>
      </div>
    );
  }

  if (!stats) {
    return (
      <div className="admin-page">
        <p>Loading…</p>
      </div>
    );
  }

  const albumViews = stats.histogram.reduce((sum, bucket) => sum + bucket.viewCount * bucket.photoCount, 0);
  const average = stats.photoCount > 0 ? (albumViews / stats.photoCount).toFixed(1) : '–';

  return (
    <div className="admin-page stats-page">
      {back}
      <h1>Stats – {stats.channelName}</h1>

      <div className="stats-tiles">
        <div className="stats-tile">
          <span className="stats-tile-value">{stats.totalViews}</span>
          <span className="stats-tile-label">Total views</span>
        </div>
        <div className="stats-tile">
          <span className="stats-tile-value">{stats.photoCount}</span>
          <span className="stats-tile-label">Photos in album</span>
        </div>
        <div className="stats-tile">
          <span className="stats-tile-value">{stats.neverViewedCount}</span>
          <span className="stats-tile-label">Never shown</span>
        </div>
        <div className="stats-tile">
          <span className="stats-tile-value">{average}</span>
          <span className="stats-tile-label">Average views per photo</span>
        </div>
      </div>

      <section className="stats-card">
        <h2>How often photos have been shown</h2>
        {stats.histogram.length > 0 ? <Histogram histogram={stats.histogram} /> : <p>Nothing has been shown yet.</p>}
      </section>

      <div className="stats-lists">
        <PhotoList title="Most viewed" items={stats.topViewed} albumId={stats.albumId} />
        <PhotoList title="Least viewed" items={stats.leastViewed} albumId={stats.albumId} />
      </div>
    </div>
  );
}
