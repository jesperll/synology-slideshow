import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { StatsPage } from './StatsPage';
import * as api from '../services/api';
import { ChannelStats } from '../types';

vi.mock('../services/api');

const stats: ChannelStats = {
  totalViews: 12,
  photoCount: 5,
  neverViewedCount: 2,
  channelName: 'kitchen',
  albumId: 7,
  histogram: [
    { viewCount: 0, photoCount: 2 },
    { viewCount: 1, photoCount: 1 },
    { viewCount: 2, photoCount: 0 },
    { viewCount: 3, photoCount: 2 }
  ],
  topViewed: [{ slideId: 10, viewCount: 3 }],
  leastViewed: [{ slideId: 20, viewCount: 0 }, { slideId: 30, viewCount: 1 }]
};

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/admin/channels/4/stats']}>
      <Routes>
        <Route path="/admin/channels/:channelId/stats" element={<StatsPage />} />
      </Routes>
    </MemoryRouter>
  );

describe('StatsPage', () => {
  beforeEach(() => {
    vi.mocked(api.getChannelStats).mockReset().mockResolvedValue({ data: stats } as any);
  });

  it("loads the channel's stats and shows its name and summary tiles", async () => {
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Stats – kitchen' })).toBeInTheDocument();
    expect(api.getChannelStats).toHaveBeenCalledWith(4);
    expect(screen.getByText('Total views').previousSibling).toHaveTextContent('12');
    expect(screen.getByText('Photos in album').previousSibling).toHaveTextContent('5');
    expect(screen.getByText('Never shown').previousSibling).toHaveTextContent('2');
    // (0*2 + 1*1 + 2*0 + 3*2) / 5 photos
    expect(screen.getByText('Average views per photo').previousSibling).toHaveTextContent('1.4');
  });

  it('draws one histogram bar per view count, including counts nobody reached', async () => {
    renderPage();

    const histogram = await screen.findByRole('list', { name: /number of photos by how many times/i });
    const bars = within(histogram).getAllByRole('listitem');

    expect(bars.map((bar) => bar.textContent)).toEqual(['20', '11', '02', '23']);
    expect(bars[0]).toHaveClass('never-shown');
    expect(bars[1]).not.toHaveClass('never-shown');
  });

  it('collapses very high view counts into a single tail bar', async () => {
    vi.mocked(api.getChannelStats).mockResolvedValue({
      data: {
        ...stats,
        histogram: Array.from({ length: 40 }, (_, viewCount) => ({ viewCount, photoCount: 1 }))
      }
    } as any);
    renderPage();

    const histogram = await screen.findByRole('list', { name: /number of photos by how many times/i });
    const bars = within(histogram).getAllByRole('listitem');

    expect(bars).toHaveLength(26);
    expect(bars[25].textContent).toBe('1525+'); // 15 photos had 25..39 views
  });

  it('shows a thumbnail only while a photo row is hovered', async () => {
    renderPage();
    const row = (await screen.findByText('Photo #10')).closest('li')!;

    expect(screen.queryByRole('img')).not.toBeInTheDocument();

    fireEvent.mouseEnter(row);
    expect(screen.getByRole('img', { name: 'Photo #10' })).toHaveAttribute('src', '/api/albums/7/slides/10/thumbnail.jpg');

    fireEvent.mouseLeave(row);
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('toggles the thumbnail on tap for touch screens', async () => {
    renderPage();
    const row = (await screen.findByText('Photo #20')).closest('li')!;

    fireEvent.click(row);
    expect(screen.getByRole('img', { name: 'Photo #20' })).toBeInTheDocument();

    fireEvent.click(row);
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('labels photos by how often they were shown', async () => {
    renderPage();

    expect(await screen.findByText('3 views')).toBeInTheDocument();
    expect(screen.getByText('never shown')).toBeInTheDocument();
    expect(screen.getByText('1 view')).toBeInTheDocument();
  });

  it('says so when nothing has been shown yet', async () => {
    vi.mocked(api.getChannelStats).mockResolvedValue({
      data: { ...stats, totalViews: 0, photoCount: 0, neverViewedCount: 0, histogram: [], topViewed: [], leastViewed: [] }
    } as any);
    renderPage();

    expect(await screen.findByText('Nothing has been shown yet.')).toBeInTheDocument();
    expect(screen.getByText('Average views per photo').previousSibling).toHaveTextContent('–');
  });

  it('shows an error and a way back when the stats cannot be loaded', async () => {
    vi.mocked(api.getChannelStats).mockRejectedValue(new Error('404'));
    renderPage();

    expect(await screen.findByText('Could not load stats for this channel.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('link', { name: '← All channels' })).toHaveAttribute('href', '/admin'));
  });
});
