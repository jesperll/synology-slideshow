import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getChannels } from '../services/api';
import { ChannelSummary } from '../types';

export function JoinChannelPicker() {
  const navigate = useNavigate();
  const [channels, setChannels] = useState<ChannelSummary[]>([]);

  useEffect(() => {
    getChannels()
      .then((response) => setChannels(response.data))
      .catch(() => setChannels([]));
  }, []);

  if (channels.length === 0) {
    return null;
  }

  return (
    <div className="channel-picker">
      <h3>Point this device at a channel</h3>
      <ul className="channel-picker-list">
        {channels.map((channel) => (
          <li key={channel.id}>
            <button onClick={() => navigate(`/${channel.name}`)}>{channel.name}</button>
          </li>
        ))}
      </ul>
    </div>
  );
}
