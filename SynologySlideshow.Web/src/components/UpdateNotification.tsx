import React from 'react';

interface UpdateNotificationProps {
  onReload: () => void;
  onDismiss: () => void;
}

export function UpdateNotification({ onReload, onDismiss }: UpdateNotificationProps) {
  return (
    <div className="update-notification">
      <div className="update-notification-content">
        <span className="update-notification-text">Update available</span>
        <button className="update-notification-btn update-notification-btn-primary" onClick={onReload}>
          Reload
        </button>
        <button className="update-notification-btn update-notification-btn-secondary" onClick={onDismiss}>
          ✕
        </button>
      </div>
    </div>
  );
}
