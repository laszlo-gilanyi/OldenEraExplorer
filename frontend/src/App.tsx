import { useState, useEffect, useRef, Suspense } from 'react';
import { Outlet } from 'react-router-dom';
import { useExtractionProgress, useExtractionStore } from '@/features/extraction';
import { useGameStatus, useLoadGameData, useSetGamePath, useGameDetect, useLabels } from '@/hooks';
import { useSettings, useAutoUpdateChecker } from '@/features/settings';
import { useGameStore } from '@/stores';
import { extractionApi } from '@/api/client';
import { AssetExtractionPanel } from '@/features/extraction';
import { LoadingScreen } from '@/components/feedback';
import { SettingsPanel } from '@/features/settings';
import { GlobalSearch } from '@/features/search';
import { Navigation } from '@/components/layout';

function App() {
  const { label } = useLabels();
  const [loadingMessage, setLoadingMessage] = useState(label('loading_checking_config'));
  const autoDetectTriggeredRef = useRef(false);
  const autoExtractTriggeredRef = useRef(false);

  useExtractionProgress();
  useAutoUpdateChecker();

  const { data: settings, isLoading: settingsLoading } = useSettings();

  const { data: gameStatus, isLoading: gameStatusLoading } = useGameStatus();
  const setGameReady = useGameStore((state) => state.setGameReady);

  // API client request interceptor blocks calls when game not ready, so sync this state
  useEffect(() => {
    setGameReady(gameStatus?.dataLoaded ?? false);
  }, [gameStatus?.dataLoaded, setGameReady]);

  const loadDataMutation = useLoadGameData();
  const setPathMutation = useSetGamePath();
  const detectMutation = useGameDetect();

  const extractionStatus = useExtractionStore((state) => state.status);

  // Auto-detection always runs on startup unless path already configured
  useEffect(() => {
    if (settingsLoading || gameStatusLoading) return;
    if (autoDetectTriggeredRef.current) return;

    // Case 1: Path configured but data not loaded yet
    if (gameStatus?.pathSet && !gameStatus?.dataLoaded && !loadDataMutation.isPending) {
      autoDetectTriggeredRef.current = true;
      // eslint-disable-next-line react-hooks/set-state-in-effect -- Need to update loading message during state transition
      setLoadingMessage(label('loading_game_data'));
      loadDataMutation.mutate();
      return;
    }

    // Case 2: No path configured - detect Steam installation, select best candidate, then load
    if (!gameStatus?.pathSet && !detectMutation.isPending && !setPathMutation.isPending) {
      autoDetectTriggeredRef.current = true;
      setLoadingMessage(label('loading_detecting'));

      detectMutation.mutate(undefined, {
        onSuccess: (result) => {
          if (result.success && result.selectedGameRoot) {
            setLoadingMessage(label('loading_setting_path'));
            setPathMutation.mutate(
              { path: result.selectedGameRoot, locale: settings?.locale },
              {
                onSuccess: () => {
                  setLoadingMessage(label('loading_game_data'));
                  loadDataMutation.mutate();
                },
              }
            );
          }
          // No game found - app skeleton loads, user can browse manually in Settings
        },
      });
    }
  }, [
    settings?.locale,
    gameStatus?.pathSet,
    gameStatus?.dataLoaded,
    settingsLoading,
    gameStatusLoading,
    loadDataMutation,
    setPathMutation,
    detectMutation,
    label,
  ]);

  const prevGameRootRef = useRef<string | null | undefined>(undefined);

  // Auto-extract must re-trigger when user switches game version via Settings modal
  useEffect(() => {
    // Reset trigger when gameRoot changes (detects game switches)
    if (prevGameRootRef.current !== undefined && gameStatus?.gameRoot !== prevGameRootRef.current) {
      autoExtractTriggeredRef.current = false;
    }
    prevGameRootRef.current = gameStatus?.gameRoot;

    if (!gameStatus?.pathSet || !gameStatus?.dataLoaded) {
      autoExtractTriggeredRef.current = false;
      return;
    }

    // Race condition prevention: settings load async, must wait for actual data
    if (settings === undefined) {
      return;
    }

    if (!settings.autoExtractEnabled) return;
    if (autoExtractTriggeredRef.current) return;
    if (extractionStatus === 'Extracting') return;
    if (!settings.extractPng && !settings.extractGlb) return;

    autoExtractTriggeredRef.current = true;
    console.log('[Auto-extract] Starting extraction with settings:', {
      extractPng: settings.extractPng,
      extractGlb: settings.extractGlb,
      gameRoot: gameStatus.gameRoot,
    });

    // Manifest cache prevents re-extraction if assets already exist for this version
    extractionApi.start({
      extractPng: settings.extractPng,
      extractGlb: settings.extractGlb,
      forceReExtract: false,
    }).catch((err) => {
      console.error('Auto-extraction failed:', err);
      autoExtractTriggeredRef.current = false;
    });
  }, [
    gameStatus?.pathSet,
    gameStatus?.dataLoaded,
    gameStatus?.gameRoot,
    settings,
    extractionStatus,
  ]);

  if (gameStatusLoading || settingsLoading) {
    return <LoadingScreen message={label('loading_checking_config')} />;
  }

  const isAutoProcessing =
    detectMutation.isPending ||
    setPathMutation.isPending ||
    (gameStatus?.pathSet && !gameStatus?.dataLoaded && loadDataMutation.isPending);

  if (isAutoProcessing) {
    return <LoadingScreen message={loadingMessage} />;
  }

  // Child routes expect game data to be available - show loading until ready to prevent API errors
  if (!gameStatus?.dataLoaded) {
    return <LoadingScreen message={label('loading_game_data')} />;
  }
  return (
    <div className="h-screen flex flex-col overflow-hidden">
      <header className="px-6 py-4 border-b border-border bg-card flex justify-between items-center relative">
        <div className="flex items-center gap-4 max-[1050px]:gap-3">
          <img src="/icon.png" alt="Logo" className="w-8 h-8 max-[1050px]:w-7 max-[1050px]:h-7" />
          <h1 className="m-0 text-2xl max-[1050px]:text-xl text-foreground">{label('app_title')}</h1>
        </div>
        <div className="flex items-center gap-3">
          <GlobalSearch />
          <AssetExtractionPanel />
          <SettingsPanel />
        </div>
      </header>
      <div className="flex-1 flex flex-col relative overflow-hidden">
        <Navigation />
        <main className="flex-1 overflow-hidden">
          <Suspense fallback={<LoadingScreen message={label('loading_game_data')} />}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </div>
  );
}

export default App;
