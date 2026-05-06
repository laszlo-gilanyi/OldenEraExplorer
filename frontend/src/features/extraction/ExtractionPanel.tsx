import { useState, useEffect, useRef } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useExtractionStore } from './extractionStore';
import { extractionApi } from '@/api/client';
import { useSettings, useUpdateSettings } from '@/features/settings/useSettings';
import { cn } from '@/lib/utils';
import { useLabels } from '@/hooks/useLabels';
import { useOnClickOutside } from '@/hooks/useOnClickOutside';

function formatElapsed(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
}

function formatRelativeTime(dateString: string, label: (key: string, ...args: (string | number)[]) => string): string {
  const date = new Date(dateString);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMins / 60);
  const diffDays = Math.floor(diffHours / 24);

  if (diffMins < 1) return label('extraction_time_just_now');
  if (diffMins < 60) return label('extraction_time_mins_ago', diffMins);
  if (diffHours < 24) return label('extraction_time_hours_ago', diffHours);
  if (diffDays < 7) return label('extraction_time_days_ago', diffDays);
  return date.toLocaleDateString();
}

export function AssetExtractionPanel() {
  const { label } = useLabels();

  const {
    status,
    progress,
    isConnected,
    lastError,
    isPanelOpen,
    manifestInfo,
    setPanelOpen
  } = useExtractionStore();

  const hasAssets = (manifestInfo?.iconCount ?? 0) > 0 || (manifestInfo?.modelCount ?? 0) > 0;

  const { data: settings } = useSettings();
  const updateSettings = useUpdateSettings();

  const extractPng = settings?.extractPng ?? true;
  const extractGlb = settings?.extractGlb ?? true;

  const [forceReExtract, setForceReExtract] = useState(false);
  const [showOptions, setShowOptions] = useState(false);
  const [showErrorPanel, setShowErrorPanel] = useState(false);
  const [copiedFlash, setCopiedFlash] = useState(false);
  const optionsPanelRef = useRef<HTMLDivElement>(null);
  const progressPanelRef = useRef<HTMLDivElement>(null);
  const errorPanelRef = useRef<HTMLDivElement>(null);
  const optionsButtonRef = useRef<HTMLButtonElement>(null);
  const progressButtonRef = useRef<HTMLButtonElement>(null);
  const failedButtonRef = useRef<HTMLButtonElement>(null);

  const setExtractPng = (value: boolean) => {
    updateSettings.mutate({ extractPng: value });
  };

  const setExtractGlb = (value: boolean) => {
    updateSettings.mutate({ extractGlb: value });
  };

  const [localElapsed, setLocalElapsed] = useState(0);
  const startTimeRef = useRef<number | null>(null);


  const startMutation = useMutation({
    mutationFn: () => extractionApi.start({ extractPng, extractGlb, forceReExtract }),
  });

  const cancelMutation = useMutation({
    mutationFn: () => extractionApi.cancel(),
  });

  const isExtracting = status === 'Extracting';
  const canStart = !isExtracting && isConnected;
  const isInitializing = isExtracting && (!progress || progress.total === 0);

  useOnClickOutside(optionsPanelRef, () => setShowOptions(false), [optionsButtonRef]);
  useOnClickOutside(progressPanelRef, () => setPanelOpen(false), [progressButtonRef]);
  // Click-outside closes the error panel but does NOT clear the error or the Failed state.
  // The user can reopen the panel via the Failed button. Only Dismiss clears the error.
  useOnClickOutside(errorPanelRef, () => setShowErrorPanel(false), [failedButtonRef]);

  useEffect(() => {
    if (isExtracting) {
      if (startTimeRef.current === null) {
        startTimeRef.current = Date.now();
        // Reset elapsed time when extraction starts - intentional for timer sync
        // eslint-disable-next-line react-hooks/set-state-in-effect
        setLocalElapsed(0);
      }

      const intervalId = setInterval(() => {
        if (startTimeRef.current !== null) {
          const elapsed = Math.floor((Date.now() - startTimeRef.current) / 1000);
          setLocalElapsed(elapsed);
        }
      }, 1000);

      return () => clearInterval(intervalId);
    } else {
      startTimeRef.current = null;
    }
  }, [isExtracting]);

  useEffect(() => {
    // Failed is sticky: the user must explicitly dismiss it via the error panel so the
    // failure (and its diagnostics) does not silently disappear behind a 300ms timer.
    if (status === 'Completed' || status === 'Cancelled') {
      const timeout = 300;
      const timeoutId = setTimeout(() => {
        useExtractionStore.getState().setStatus('Idle');
        useExtractionStore.getState().setCompletionStats(null);
      }, timeout);
      return () => clearTimeout(timeoutId);
    }
  }, [status]);

  const handleCopyError = async () => {
    if (!lastError) return;
    try {
      await navigator.clipboard.writeText(lastError);
      setCopiedFlash(true);
      setTimeout(() => setCopiedFlash(false), 1500);
    } catch (err) {
      console.error('[OldenEraExplorer] Failed to copy error to clipboard:', err);
    }
  };

  const handleDismissError = () => {
    setShowErrorPanel(false);
    useExtractionStore.getState().setError(null);
    useExtractionStore.getState().setStatus('Idle');
    useExtractionStore.getState().setCompletionStats(null);
    // Backend keeps the terminal status until something resets it; without this call,
    // a SignalR reconnect (tab refresh, network blip) would re-push Failed and revive
    // the panel the user just dismissed.
    extractionApi.dismiss().catch(() => {
      // Backend reset is best-effort: the local UI is already cleared.
    });
  };

  const handleStart = () => {
    setShowOptions(false);
    setPanelOpen(true);
    useExtractionStore.getState().setProgress(null);
    useExtractionStore.getState().setCompletionStats(null);
    startMutation.mutate();
  };

  const handleCancel = () => {
    cancelMutation.mutate();
  };

  const togglePanel = (e?: React.MouseEvent) => {
    e?.stopPropagation();
    setPanelOpen(!isPanelOpen);
  };

  const renderButton = () => {
    if (!isConnected) {
      return (
        <button
          disabled
          className="h-[47px] px-3 bg-muted text-muted-foreground border-none rounded-xl text-lg cursor-not-allowed opacity-70 flex items-center gap-2"
        >
          <span className="w-2 h-2 rounded-full bg-destructive" />
          {label('extraction_offline')}
        </button>
      );
    }

    if (isExtracting && !isPanelOpen) {
      return (
        <button
          ref={progressButtonRef}
          onClick={togglePanel}
          className="h-[47px] px-3 rounded-xl text-lg cursor-pointer flex items-center gap-2 transition-colors border-none bg-transparent text-muted-foreground hover:text-foreground hover:bg-accent"
        >
          <svg className="animate-spin h-8 w-8 text-primary" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
          </svg>
          <span>{progress?.phase ?? label('extraction_initializing')}</span>
        </button>
      );
    }

    if (isExtracting) {
      return (
        <button
          ref={progressButtonRef}
          onClick={togglePanel}
          className="h-[47px] px-3 rounded-xl text-lg cursor-pointer flex items-center gap-2 transition-colors border-none bg-transparent text-muted-foreground hover:text-foreground hover:bg-accent"
        >
          <svg className="animate-spin h-8 w-8 text-primary" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
          </svg>
          <span>{label('extraction_extracting')}</span>
        </button>
      );
    }

    if (status === 'Completed') {
      return (
        <button className="h-[47px] px-3 rounded-xl text-lg flex items-center gap-2">
          <svg className="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 21l-8 -4.5v-9l8 -4.5l8 4.5v4.5" />
            <path d="M12 12l8 -4.5" />
            <path d="M12 12v9" />
            <path d="M12 12l-8 -4.5" />
            <path d="M15 18h7" />
            <path d="M19 15l3 3l-3 3" />
          </svg>
          <span className="text-semantic-green font-semibold">{label('extraction_done')}</span>
        </button>
      );
    }

    if (status === 'Failed') {
      return (
        <button
          ref={failedButtonRef}
          onClick={() => setShowErrorPanel(!showErrorPanel)}
          className={cn(
            "h-[47px] px-3 rounded-xl text-lg flex items-center gap-2 transition-colors border-none bg-transparent cursor-pointer",
            showErrorPanel ? "bg-accent" : "hover:bg-accent"
          )}
        >
          <svg className="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 21l-8 -4.5v-9l8 -4.5l8 4.5v4.5" />
            <path d="M12 12l8 -4.5" />
            <path d="M12 12v9" />
            <path d="M12 12l-8 -4.5" />
            <path d="M15 18h7" />
            <path d="M19 15l3 3l-3 3" />
          </svg>
          <span className="text-destructive font-semibold">{label('extraction_failed')}</span>
        </button>
      );
    }

    if (status === 'Cancelled') {
      return (
        <button className="h-[47px] px-3 rounded-xl text-lg flex items-center gap-2">
          <svg className="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M12 21l-8 -4.5v-9l8 -4.5l8 4.5v4.5" />
            <path d="M12 12l8 -4.5" />
            <path d="M12 12v9" />
            <path d="M12 12l-8 -4.5" />
            <path d="M15 18h7" />
            <path d="M19 15l3 3l-3 3" />
          </svg>
          <span className="text-destructive font-semibold">{label('extraction_cancelled')}</span>
        </button>
      );
    }

    return (
      <button
        ref={optionsButtonRef}
        onClick={() => setShowOptions(!showOptions)}
        disabled={!canStart}
        className={cn(
          "h-[47px] px-3 rounded-xl text-lg flex items-center gap-2 transition-colors border-none bg-transparent relative z-[101]",
          showOptions
            ? "text-foreground"
            : canStart
              ? "text-muted-foreground hover:text-foreground hover:bg-accent cursor-pointer"
              : "text-muted-foreground cursor-not-allowed opacity-70"
        )}
      >
        <svg className="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M12 21l-8 -4.5v-9l8 -4.5l8 4.5v4.5" />
          <path d="M12 12l8 -4.5" />
          <path d="M12 12v9" />
          <path d="M12 12l-8 -4.5" />
          <path d="M15 18h7" />
          <path d="M19 15l3 3l-3 3" />
        </svg>
        <span>{label('extraction_btn_extract')}</span>
        <svg
          className={cn("h-5 w-5 opacity-60 transition-transform", showOptions && "rotate-180")}
          viewBox="0 0 20 20"
          fill="currentColor"
        >
          <path fillRule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clipRule="evenodd" />
        </svg>
      </button>
    );
  };

  return (
    <div className="relative flex items-center">
      {renderButton()}

      {showOptions && !isExtracting && (
        <>
          <div ref={optionsPanelRef} className="absolute top-0 right-0 bg-popover border border-border rounded-xl shadow-[0_8px_32px_rgba(0,0,0,0.5)] z-[100] min-w-[312px] overflow-hidden">
          <div className="px-4 py-1.5">

            <div className="text-xs mt-1 space-y-0.5">
              {!isConnected ? (
                <div className="text-destructive font-semibold">{label('extraction_offline')}</div>
              ) : hasAssets ? (
                <>
                  <div>
                    <span className="text-semantic-green font-semibold">{label('extraction_done')}</span>
                    {manifestInfo.lastExtractedAt && (
                      <span className="text-muted-foreground"> ({formatRelativeTime(manifestInfo.lastExtractedAt, label)})</span>
                    )}
                  </div>
                  <div className="text-muted-foreground">
                    {label('extraction_icons_count', manifestInfo.iconCount, manifestInfo.modelCount)}
                  </div>
                  {manifestInfo.gameVersion && (
                    <div className="text-muted-foreground/70 text-[11px]">
                      {manifestInfo.gameVersion}
                    </div>
                  )}
                </>
              ) : lastError ? (
                <div className="text-destructive font-semibold">{label('extraction_error')}</div>
              ) : (
                <div className="text-semantic-orange font-semibold">{label('extraction_not_extracted')}</div>
              )}
            </div>
          </div>

          <div className="p-3 space-y-3">
            <div className="flex items-center justify-between">
              <div>
                <div className="text-sm font-medium text-foreground">{label('extraction_icons')}</div>
              </div>
              <button
                type="button"
                role="switch"
                aria-checked={extractPng}
                onClick={() => setExtractPng(!extractPng)}
                className={cn(
                  "relative w-9 h-5 rounded-full transition-colors flex items-center px-0.5 cursor-pointer",
                  extractPng ? "bg-primary" : "bg-muted-foreground/40"
                )}
              >
                <span
                  className={cn(
                    "w-3.5 h-3.5 bg-white rounded-full shadow transition-transform",
                    extractPng && "translate-x-[18px]"
                  )}
                />
              </button>
            </div>

            <div className="flex items-center justify-between">
              <div>
                <div className="text-sm font-medium text-foreground">{label('extraction_models')}</div>
              </div>
              <button
                type="button"
                role="switch"
                aria-checked={extractGlb}
                onClick={() => setExtractGlb(!extractGlb)}
                className={cn(
                  "relative w-9 h-5 rounded-full transition-colors flex items-center px-0.5 cursor-pointer",
                  extractGlb ? "bg-primary" : "bg-muted-foreground/40"
                )}
              >
                <span
                  className={cn(
                    "w-3.5 h-3.5 bg-white rounded-full shadow transition-transform",
                    extractGlb && "translate-x-[18px]"
                  )}
                />
              </button>
            </div>
          </div>

          <div className="px-4 py-2 border-t border-border">
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted-foreground">{label('extraction_replace')}</span>
              <button
                type="button"
                role="switch"
                aria-checked={forceReExtract}
                onClick={() => setForceReExtract(!forceReExtract)}
                className={cn(
                  "relative w-9 h-5 rounded-full transition-colors flex items-center px-0.5 cursor-pointer",
                  forceReExtract ? "bg-primary" : "bg-muted-foreground/40"
                )}
              >
                <span
                  className={cn(
                    "w-3.5 h-3.5 bg-white rounded-full shadow transition-transform",
                    forceReExtract && "translate-x-[18px]"
                  )}
                />
              </button>
            </div>
          </div>

          <div className="p-2 border-t border-border bg-muted/30">
            <button
              onClick={handleStart}
              disabled={!extractPng && !extractGlb}
              className={cn(
                "w-full px-4 py-2 rounded-md text-sm font-medium transition-colors",
                !extractPng && !extractGlb
                  ? "bg-muted text-muted-foreground cursor-not-allowed"
                  : "bg-primary text-white cursor-pointer hover:bg-primary/90"
              )}
            >
              {label('extraction_btn_start')}
            </button>
          </div>
          </div>
        </>
      )}

      {isExtracting && isPanelOpen && (
        <>
          <div ref={progressPanelRef} className="absolute top-full right-0 mt-2 p-4 bg-popover border border-border rounded-xl shadow-[0_8px_32px_rgba(0,0,0,0.5)] z-[100] min-w-[432px]">
          <div className="flex items-center justify-between mb-3">
            <div className="flex items-center gap-2">
              <svg className="animate-spin h-4 w-4 text-primary" viewBox="0 0 24 24" fill="none">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              <span className="font-medium text-foreground">{label('extraction_extracting')}</span>
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={handleCancel}
                disabled={cancelMutation.isPending}
                className={cn(
                  "px-2 py-1 bg-destructive text-white border-none rounded text-xs",
                  cancelMutation.isPending
                    ? "cursor-not-allowed opacity-70"
                    : "cursor-pointer hover:bg-destructive/90"
                )}
              >
                {cancelMutation.isPending ? label('extraction_btn_cancelling') : label('extraction_btn_cancel')}
              </button>
            </div>
          </div>

          <div className="text-sm text-foreground mb-2">
            {label('extraction_phase')} <span className="font-medium">{progress?.phase ?? label('extraction_initializing')}</span>
          </div>

          {!isInitializing && (
            <div className="mb-3">
              <div className="h-2.5 bg-muted rounded-full overflow-hidden">
                <div
                  className="h-full bg-primary transition-all duration-300 ease-out rounded-full"
                  style={{ width: `${progress?.percent ?? 0}%` }}
                />
              </div>
              <div className="flex justify-between mt-1 text-xs text-foreground">
                <span>{progress?.current ?? 0} / {progress?.total ?? 0}</span>
                <span>{progress?.percent.toFixed(1) ?? 0}%</span>
              </div>
            </div>
          )}

          {!isInitializing && progress?.currentAsset && (
            <div className="text-sm mb-2">
              <span className="text-foreground">{label('extraction_current')} </span>
              <span className="font-medium">{progress.currentAsset}</span>
            </div>
          )}

          <div className={cn(
            "flex justify-between text-xs text-foreground",
            !isInitializing && "border-t border-border pt-2 mt-2"
          )}>
            <span>{label('extraction_elapsed')} {formatElapsed(localElapsed)}</span>
            {!isInitializing && progress?.estimatedRemaining && (
              <span>{label('extraction_remaining', progress.estimatedRemaining)}</span>
            )}
          </div>
          </div>
        </>
      )}

      {status === 'Failed' && lastError && showErrorPanel && (
        <div
          ref={errorPanelRef}
          className="absolute top-full right-0 mt-2 bg-popover border border-destructive rounded-xl shadow-[0_8px_32px_rgba(0,0,0,0.5)] z-[100] w-[520px] max-w-[90vw] overflow-hidden"
        >
          <div className="flex items-center justify-between px-4 py-2 border-b border-border bg-destructive/10">
            <div className="flex items-center gap-2 text-destructive font-semibold text-sm">
              <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" />
                <line x1="12" y1="8" x2="12" y2="12" />
                <line x1="12" y1="16" x2="12.01" y2="16" />
              </svg>
              <span>{label('extraction_failed')}</span>
            </div>
            <div className="flex items-center gap-1">
              <button
                onClick={handleCopyError}
                className="p-1.5 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                aria-label="Copy"
                title="Copy"
              >
                {copiedFlash ? (
                  <svg className="w-4 h-4 text-semantic-green" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                ) : (
                  <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
                    <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                  </svg>
                )}
              </button>
              <button
                onClick={handleDismissError}
                className="p-1.5 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                aria-label="Dismiss"
                title="Dismiss"
              >
                <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <line x1="18" y1="6" x2="6" y2="18" />
                  <line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            </div>
          </div>
          <div className="p-4 max-h-[400px] overflow-auto">
            <pre className="text-xs text-foreground whitespace-pre-wrap break-words font-mono select-text">
              {lastError}
            </pre>
          </div>
        </div>
      )}
    </div>
  );
}
