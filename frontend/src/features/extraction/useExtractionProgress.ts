import { useEffect, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { useExtractionStore } from './extractionStore';
import type { ExtractionStatus, ExtractionProgress } from './extractionStore';
import { useImageStore } from '@/stores/imageStore';

const REFRESH_INTERVAL = 50;

export function useExtractionProgress() {
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const lastRefreshAtRef = useRef<number>(0);
  const { setStatus, setProgress, setConnected, setError, setManifestInfo } = useExtractionStore();

  const connect = useCallback(async () => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/ws/extraction')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    connection.onreconnecting(() => {
      setConnected(false);
      console.log('SignalR reconnecting...');
    });

    connection.onreconnected(() => {
      setConnected(true);
      console.log('SignalR reconnected');
    });

    connection.onclose(() => {
      setConnected(false);
      console.log('SignalR disconnected');
    });

    connection.on('ExtractionStatus', (data: {
      status: string;
      progress: ExtractionProgress | null;
      lastExtractedAt: string | null;
      iconCount: number;
      modelCount: number;
      gameVersion: string | null;
      error: string | null;
    }) => {
      const prevStatus = useExtractionStore.getState().status;
      setStatus(data.status as ExtractionStatus);
      setProgress(data.progress);
      setError(data.error ?? null);
      setManifestInfo({
        lastExtractedAt: data.lastExtractedAt,
        iconCount: data.iconCount,
        modelCount: data.modelCount,
        gameVersion: data.gameVersion,
        error: data.error ?? null,
      });

      // Mirror failure-state events to the dev console so they show up where developers
      // reflexively look. The crash log file remains the comprehensive deliverable.
      if (data.status === 'Failed' && prevStatus !== 'Failed') {
        console.error('[OldenEraExplorer] Extraction failed:', data.error ?? '(no error message)');
      } else if (data.status === 'Cancelled' && prevStatus !== 'Cancelled') {
        console.warn('[OldenEraExplorer] Extraction cancelled');
      }

      if (data.status === 'Extracting' && prevStatus !== 'Extracting') {
        lastRefreshAtRef.current = 0;
      }

      if (data.status === 'Completed' && prevStatus === 'Extracting') {
        useImageStore.getState().triggerRetry();
      }
    });

    connection.on('ExtractionProgress', (progress: ExtractionProgress) => {
      setProgress(progress);

      if (progress.current > 0) {
        const shouldRefresh =
          Math.floor(progress.current / REFRESH_INTERVAL) > Math.floor(lastRefreshAtRef.current / REFRESH_INTERVAL) ||
          (lastRefreshAtRef.current < progress.total / 2 && progress.current >= progress.total / 2) ||
          progress.current === progress.total;

        if (shouldRefresh) {
          lastRefreshAtRef.current = progress.current;
          useImageStore.getState().triggerRetry();
        }
      }
    });

    connection.on('AssetExtracted', (data: { assetPath: string; assetType: string }) => {
      if (data.assetType === 'png') {
        useImageStore.getState().markLoaded(data.assetPath);
      }
    });

    connection.on('ExtractionCompleted', (data: {
      jobId: string;
      finalStatus: string;
      totalExtracted: number;
      totalSkipped: number;
      totalFailed: number;
    }) => {
      setStatus(data.finalStatus as ExtractionStatus);
      setProgress(null);
      console.log(
        `Extraction completed: ${data.totalExtracted} extracted, ${data.totalSkipped} skipped, ${data.totalFailed} failed`
      );
    });

    try {
      await connection.start();
      connectionRef.current = connection;
      setConnected(true);
      console.log('SignalR connected');
    } catch (err) {
      console.error('[OldenEraExplorer] SignalR connection failed:', err);
      setError(err instanceof Error ? err.message : 'Connection failed');
      setConnected(false);
    }
  }, [setStatus, setProgress, setConnected, setError, setManifestInfo]);

  const disconnect = useCallback(async () => {
    if (connectionRef.current) {
      await connectionRef.current.stop();
      connectionRef.current = null;
      setConnected(false);
    }
  }, [setConnected]);

  useEffect(() => {
    connect();
    return () => {
      disconnect();
    };
  }, [connect, disconnect]);

  return { connect, disconnect };
}
