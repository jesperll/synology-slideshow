import { describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { JoinChannelPicker } from './JoinChannelPicker';
import * as api from '../services/api';

vi.mock('../services/api');

describe('JoinChannelPicker', () => {
  it('lists channels fetched from the server and navigates to the selected one', async () => {
    vi.mocked(api.getChannels).mockResolvedValue({ data: [{ id: 1, name: 'kitchen' }] } as any);

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<JoinChannelPicker />} />
          <Route path="/:channelName" element={<div>joined channel page</div>} />
        </Routes>
      </MemoryRouter>
    );

    const button = await screen.findByRole('button', { name: 'kitchen' });
    fireEvent.click(button);

    expect(await screen.findByText('joined channel page')).toBeInTheDocument();
  });

  it('renders nothing when there are no channels yet', async () => {
    vi.mocked(api.getChannels).mockResolvedValue({ data: [] } as any);

    const { container } = render(
      <MemoryRouter>
        <JoinChannelPicker />
      </MemoryRouter>
    );

    await waitFor(() => expect(api.getChannels).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });
});
