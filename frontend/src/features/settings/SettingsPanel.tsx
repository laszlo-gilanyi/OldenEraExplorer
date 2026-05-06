import { useState, useEffect, useCallback, useRef } from 'react';
import { useSettings, useUpdateSettings, useLocales, useCheckUpdate, useInstallUpdate, useUpdateProgress } from './useSettings';
import { useGameStatus, useGameDetect, useSetGamePath, useLoadGameData } from '@/hooks/useGameStatus';
import { FolderPickerModal } from '@/components/ui/FolderPickerModal';
import { useOnClickOutside } from '@/hooks/useOnClickOutside';
import { cn } from '@/lib/utils';
import { LOCALE_DISPLAY_NAMES } from '@/lib/localeMapping';
import { useLabels } from '@/hooks/useLabels';
import type { CandidateDto } from '@/api/types';

// Insert word break opportunities after path separators
function BreakablePath({ path }: { path: string }) {
  const parts = path.split(/([/\\])/);
  return (
    <>
      {parts.map((part, i) => (
        <span key={i}>
          {part}
          {(part === '/' || part === '\\') && <wbr />}
        </span>
      ))}
    </>
  );
}

function clearPageStores() {
  const storeKeys = [
    'oe-units-store',
    'oe-spells-store',
    'oe-skills-store',
    'oe-factionlaws-store',
    'oe-subclasses-store',
    'oe-artifacts-store',
    'oe-abilities-store',
    'oe-heroes-store',
    'oe-mapobjects-store',
    'oe-buildings-store',
    'viewer-settings',
  ];
  storeKeys.forEach(key => sessionStorage.removeItem(key));
}

export function SettingsPanel() {
  const [isOpen, setIsOpen] = useState(false);
  const [isGameSectionOpen, setIsGameSectionOpen] = useState(false);
  const [isLocaleDropdownOpen, setIsLocaleDropdownOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const localeDropdownRef = useRef<HTMLDivElement>(null);

  const { data: settings, isLoading: settingsLoading } = useSettings();
  const { data: localesData, isLoading: localesLoading } = useLocales();
  const { data: gameStatus } = useGameStatus();
  const updateSettings = useUpdateSettings();
  const { label } = useLabels();

  const { data: availableRelease, refetch: checkUpdate, isFetching: isCheckingUpdate } = useCheckUpdate();
  const installUpdate = useInstallUpdate();
  const [isInstalling, setIsInstalling] = useState(false);
  const { data: installProgress } = useUpdateProgress(isInstalling);

  const hasUpdate = !!availableRelease;
  const appVersion = settings?.version ?? '';

  useEffect(() => {
    if (installProgress?.stage === 'error') {
      setIsInstalling(false);
    }
  }, [installProgress?.stage]);

  const detectMutation = useGameDetect();
  const setPathMutation = useSetGamePath();
  const loadDataMutation = useLoadGameData();

  const [switchingPath, setSwitchingPath] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [folderPickerOpen, setFolderPickerOpen] = useState(false);

  const currentGameFolder = gameStatus?.gameRoot
    ? gameStatus.gameRoot.split(/[/\\]/).filter(Boolean).pop() || gameStatus.gameRoot
    : null;

  useEffect(() => {
    if (isGameSectionOpen) {
      // Reset UI state when game section opens - intentional for clean slate
       
      setError(null);
       
      setSwitchingPath(null);
      detectMutation.mutate();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGameSectionOpen]);

  useEffect(() => {
    if (isOpen && !currentGameFolder) {
      detectMutation.mutate();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, currentGameFolder]);

  const handleSelectGame = useCallback((candidate: CandidateDto) => {
    if (candidate.gameRoot === gameStatus?.gameRoot) {
      return;
    }

    setError(null);
    setSwitchingPath(candidate.gameRoot);

    setPathMutation.mutate({ path: candidate.gameRoot }, {
      onSuccess: () => {
        loadDataMutation.mutate(undefined, {
          onSuccess: () => {
            clearPageStores();
            setSwitchingPath(null);
            setIsGameSectionOpen(false);
          },
          onError: (err) => {
            setSwitchingPath(null);
            setError(err.message);
          },
        });
      },
      onError: (err) => {
        setSwitchingPath(null);
        setError(err.message);
      },
    });
  }, [gameStatus?.gameRoot, setPathMutation, loadDataMutation]);

  const handleBrowse = useCallback(() => {
    setFolderPickerOpen(true);
  }, []);

  const handleFolderSelected = useCallback((path: string) => {
    setError(null);
    setSwitchingPath(path);
    setPathMutation.mutate({ path }, {
      onSuccess: () => {
        loadDataMutation.mutate(undefined, {
          onSuccess: () => {
            clearPageStores();
            setSwitchingPath(null);
            setIsGameSectionOpen(false);
          },
          onError: (err) => {
            setSwitchingPath(null);
            setError(err.message);
          },
        });
      },
      onError: (err) => {
        setSwitchingPath(null);
        setError(err.message);
      },
    });
  }, [setPathMutation, loadDataMutation]);

  const currentTheme = settings?.theme ?? 'Dark';

  const handleThemeChange = (theme: string) => {
    updateSettings.mutate({ theme });
  };

  const handleLocaleChange = (locale: string) => {
    updateSettings.mutate({ locale });
  };

  const handleResolverToggle = () => {
    updateSettings.mutate({ usePlaceholderResolver: !currentUsePlaceholderResolver });
  };

  const handleAutoExtractToggle = () => {
    updateSettings.mutate({ autoExtractEnabled: !currentAutoExtract });
  };

  const handleMinimizeToTrayToggle = () => {
    updateSettings.mutate({ minimizeToTray: !currentMinimizeToTray });
  };

  const handleAutoUpdateToggle = () => {
    updateSettings.mutate({ autoUpdateEnabled: !currentAutoUpdate });
  };

  const handleInstallUpdate = () => {
    if (!availableRelease) return;
    setIsInstalling(true);
    installUpdate.mutate(availableRelease);
  };

  const currentLocale = settings?.locale ?? 'english';
  const currentUsePlaceholderResolver = settings?.usePlaceholderResolver ?? false;
  const currentAutoExtract = settings?.autoExtractEnabled ?? false;
  const currentMinimizeToTray = settings?.minimizeToTray ?? true;
  const currentAutoUpdate = settings?.autoUpdateEnabled ?? true;

  const candidates = detectMutation.data?.candidates || [];
  const isDetecting = detectMutation.isPending;
  const isSwitching = switchingPath !== null;

  useOnClickOutside(panelRef, () => setIsOpen(false), [buttonRef]);

  useOnClickOutside(localeDropdownRef, () => setIsLocaleDropdownOpen(false));

  return (
    <div className="relative flex items-center">
      <button
        ref={buttonRef}
        onClick={() => setIsOpen(!isOpen)}
        className={cn(
          "h-[47px] px-3 rounded-xl text-lg flex items-center gap-2 transition-colors cursor-pointer border-none bg-transparent relative z-[102]",
          isOpen
            ? "text-foreground"
            : "text-muted-foreground hover:text-foreground hover:bg-accent"
        )}
      >
        <svg className="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065z" />
          <path d="M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" />
        </svg>
        <span>{label('settings_header')}</span>
        <svg
          className={cn("h-5 w-5 opacity-60 transition-transform", isOpen && "rotate-180")}
          viewBox="0 0 20 20"
          fill="currentColor"
        >
          <path fillRule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clipRule="evenodd" />
        </svg>
      </button>

      {isOpen && (
        <>
          <div ref={panelRef} className="absolute top-0 right-0 bg-popover border border-border rounded-xl shadow-[0_8px_32px_rgba(0,0,0,0.5)] z-[101] w-[320px]">
          {settingsLoading ? (
            <div className="p-4 text-muted-foreground text-sm">{label('common_loading', '')}</div>
          ) : (
            <>
              <div className="p-3 pt-8 space-y-2">
                <div className="text-sm font-medium text-foreground">{label('settings_theme')}</div>
                <div className="flex bg-muted rounded-lg p-1">
                  <button
                    onClick={() => handleThemeChange('Default')}
                    className={cn(
                      "flex-1 px-3 py-1.5 text-sm rounded-md transition-all flex items-center justify-center gap-1.5 cursor-pointer",
                      currentTheme === 'Default'
                        ? "bg-primary text-primary-foreground shadow-sm"
                        : "text-muted-foreground hover:text-foreground"
                    )}
                  >
                    <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M3 5a1 1 0 0 1 1 -1h16a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1v-10z" />
                      <path d="M7 20h10" />
                      <path d="M9 16v4" />
                      <path d="M15 16v4" />
                    </svg>
                    <span>{label('settings_theme_system')}</span>
                  </button>

                  <button
                    onClick={() => handleThemeChange('Light')}
                    className={cn(
                      "flex-1 px-3 py-1.5 text-sm rounded-md transition-all flex items-center justify-center gap-1.5 cursor-pointer",
                      currentTheme === 'Light'
                        ? "bg-primary text-primary-foreground shadow-sm"
                        : "text-muted-foreground hover:text-foreground"
                    )}
                  >
                    <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M14.828 14.828a4 4 0 1 0 -5.656 -5.656a4 4 0 0 0 5.656 5.656z" />
                      <path d="M6.343 17.657l-1.414 1.414" />
                      <path d="M6.343 6.343l-1.414 -1.414" />
                      <path d="M17.657 6.343l1.414 -1.414" />
                      <path d="M17.657 17.657l1.414 1.414" />
                      <path d="M4 12h-2" />
                      <path d="M12 4v-2" />
                      <path d="M20 12h2" />
                      <path d="M12 20v2" />
                    </svg>
                    <span>{label('settings_theme_light')}</span>
                  </button>

                  <button
                    onClick={() => handleThemeChange('Dark')}
                    className={cn(
                      "flex-1 px-3 py-1.5 text-sm rounded-md transition-all flex items-center justify-center gap-1.5 cursor-pointer",
                      currentTheme === 'Dark'
                        ? "bg-primary text-primary-foreground shadow-sm"
                        : "text-muted-foreground hover:text-foreground"
                    )}
                  >
                    <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M12 3c.132 0 .263 0 .393 0a7.5 7.5 0 0 0 7.92 12.446a9 9 0 1 1 -8.313 -12.454z" />
                      <path d="M17 4a2 2 0 0 0 2 2a2 2 0 0 0 -2 2a2 2 0 0 0 -2 -2a2 2 0 0 0 2 -2" />
                      <path d="M19 11h2m-1 -1v2" />
                    </svg>
                    <span>{label('settings_theme_dark')}</span>
                  </button>
                </div>
              </div>

              <div className="px-3 pb-3 space-y-2">
                <div className="text-sm font-medium text-foreground">{label('common_language')}</div>
                <div className="relative" ref={localeDropdownRef}>
                  <button
                    onClick={() => !localesLoading && setIsLocaleDropdownOpen(!isLocaleDropdownOpen)}
                    disabled={localesLoading}
                    className="w-full h-8 px-3 bg-background border border-border rounded-md text-sm text-foreground text-left cursor-pointer focus:outline-none focus:ring-2 focus:ring-primary disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-between"
                  >
                    <span>{LOCALE_DISPLAY_NAMES[currentLocale] || currentLocale}</span>
                    <svg
                      className={cn("h-5 w-5 text-muted-foreground transition-transform", isLocaleDropdownOpen && "rotate-180")}
                      viewBox="0 0 20 20"
                      fill="currentColor"
                    >
                      <path fillRule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clipRule="evenodd" />
                    </svg>
                  </button>
                  {isLocaleDropdownOpen && localesData?.locales && (
                    <div className="absolute z-[102] w-full mt-1 bg-popover border border-border rounded-md shadow-lg max-h-60 overflow-auto">
                      {localesData.locales.map((locale) => (
                        <button
                          key={locale}
                          onClick={() => {
                            handleLocaleChange(locale);
                            setIsLocaleDropdownOpen(false);
                          }}
                          className={cn(
                            "w-full px-3 py-2 text-sm text-left cursor-pointer transition-colors text-popover-foreground",
                            currentLocale === locale
                              ? "bg-primary/10 text-primary"
                              : "hover:bg-accent hover:text-accent-foreground"
                          )}
                        >
                          {LOCALE_DISPLAY_NAMES[locale] || locale}
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              <div className="p-3 border-t border-border space-y-3">
                <div className="flex items-center justify-between">
                  <div className="text-sm font-medium text-foreground">{label('settings_resolver')}</div>
                  <Switch checked={currentUsePlaceholderResolver} onCheckedChange={handleResolverToggle} />
                </div>

                <div className="flex items-center justify-between">
                  <div className="text-sm font-medium text-foreground">{label('extraction_auto_extract')}</div>
                  <Switch checked={currentAutoExtract} onCheckedChange={handleAutoExtractToggle} />
                </div>

                <div className="flex items-center justify-between">
                  <div className="text-sm font-medium text-foreground">{label('settings_minimize_to_tray')}</div>
                  <Switch checked={currentMinimizeToTray} onCheckedChange={handleMinimizeToTrayToggle} />
                </div>

                <div className="flex items-center justify-between">
                  <div className="text-sm font-medium text-foreground">{label('settings_auto_update')}</div>
                  <Switch checked={currentAutoUpdate} onCheckedChange={handleAutoUpdateToggle} />
                </div>
              </div>

              <div className="p-3 border-t border-border space-y-2">
                <div className="flex items-center justify-between text-xs text-muted-foreground">
                  <span>{label('settings_version')}</span>
                  <span>{appVersion || '—'}</span>
                </div>

                {isInstalling ? (
                  <div className="space-y-1.5">
                    <div className="flex items-center gap-2 text-xs text-muted-foreground">
                      <Spinner size="sm" />
                      <span>{installProgress?.message ?? 'Installing...'}</span>
                    </div>
                    {installProgress?.stage === 'error' && (
                      <div className="text-xs text-destructive">{installProgress.message}</div>
                    )}
                  </div>
                ) : hasUpdate ? (
                  <div className="space-y-1.5">
                    <div className="text-xs text-blue-500 font-medium">
                      {label('settings_update_available', availableRelease!.tagName)}
                    </div>
                    <button
                      onClick={handleInstallUpdate}
                      className="w-full py-1.5 bg-blue-500 hover:bg-blue-600 text-white text-xs font-medium rounded transition-colors flex items-center justify-center gap-1.5"
                    >
                      <svg className="w-3.5 h-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                        <polyline points="7 10 12 15 17 10" />
                        <line x1="12" y1="15" x2="12" y2="3" />
                      </svg>
                      {label('settings_update_install', availableRelease!.tagName)}
                    </button>
                  </div>
                ) : (
                  <button
                    onClick={() => checkUpdate()}
                    disabled={isCheckingUpdate}
                    className="w-full py-1.5 border border-border hover:bg-accent text-muted-foreground hover:text-foreground text-xs font-medium rounded transition-colors flex items-center justify-center gap-2 disabled:opacity-50"
                  >
                    {isCheckingUpdate ? (
                      <>
                        <Spinner size="sm" />
                        <span>{label('settings_update_checking')}</span>
                      </>
                    ) : (
                      <span>{label('settings_update_check')}</span>
                    )}
                  </button>
                )}
              </div>

              <div className="border-t border-border">
                <div className="p-3 space-y-2">
                  <div className="text-sm font-medium text-foreground">{label('settings_game_path')}</div>
                  {currentGameFolder ? (
                    <>
                      <div className="text-sm text-muted-foreground break-words">
                        <BreakablePath path={gameStatus?.gameRoot ?? ''} />
                      </div>
                      <button
                        onClick={() => setIsGameSectionOpen(!isGameSectionOpen)}
                        className={cn(
                          "w-full px-3 py-1.5 text-sm rounded flex items-center justify-center gap-1.5 transition-colors cursor-pointer",
                          isGameSectionOpen
                            ? "bg-accent text-accent-foreground"
                            : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
                        )}
                      >
                        <span>{label('settings_change_game')}</span>
                        <svg
                          className={cn("h-3.5 w-3.5 transition-transform duration-200", isGameSectionOpen && "rotate-180")}
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        >
                          <path d="M6 9l6 6l6 -6" />
                        </svg>
                      </button>
                    </>
                  ) : (
                    <div className="text-sm text-muted-foreground italic">
                      {label('settings_no_game_configured')}
                    </div>
                  )}
                </div>

                <div
                  className={cn(
                    "overflow-hidden transition-all duration-200",
                    (isGameSectionOpen || !currentGameFolder) ? "max-h-[400px] opacity-100" : "max-h-0 opacity-0"
                  )}
                >
                    <div className="px-3 pb-3 space-y-2">
                      {error && (
                        <div className="px-3 py-2 bg-destructive/10 border border-destructive rounded text-destructive text-xs">
                          {error}
                        </div>
                      )}

                      {isDetecting && (
                        <div className="flex items-center justify-center gap-2 py-4">
                          <Spinner size="sm" />
                          <span className="text-muted-foreground text-sm">{label('welcome_detecting')}</span>
                        </div>
                      )}

                      {!isDetecting && candidates.length > 0 && (
                        <div className="flex flex-col gap-1.5 max-h-[180px] overflow-y-auto">
                          {candidates.map((candidate, index) => {
                            const isCurrentGame = candidate.gameRoot === gameStatus?.gameRoot;
                            const isThisSwitching = switchingPath === candidate.gameRoot;

                            return (
                              <button
                                key={index}
                                onClick={() => handleSelectGame(candidate)}
                                disabled={isSwitching || isCurrentGame}
                                className={cn(
                                  "px-3 py-2 rounded text-left transition-all border text-sm",
                                  isCurrentGame
                                    ? "bg-primary/10 border-primary cursor-default"
                                    : isSwitching
                                      ? "bg-muted border-border opacity-50 cursor-not-allowed"
                                      : "bg-muted border-border hover:border-muted-foreground cursor-pointer"
                                )}
                              >
                                <div className="flex items-center gap-2">
                                  {isThisSwitching && <Spinner size="sm" />}
                                  {isCurrentGame && !isThisSwitching && (
                                    <svg className="w-4 h-4 text-primary flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
                                      <path d="M5 12l5 5l10 -10" />
                                    </svg>
                                  )}
                                  <span className="text-xs text-muted-foreground break-words">
                                    <BreakablePath path={candidate.gameRoot} />
                                  </span>
                                </div>
                              </button>
                            );
                          })}
                        </div>
                      )}

                      {!isDetecting && candidates.length === 0 && (
                        <div className="px-3 py-2 bg-amber-500/10 border border-amber-500 rounded text-amber-600 dark:text-amber-400 text-xs">
                          {label('welcome_no_installs')}
                        </div>
                      )}

                      {!isDetecting && (
                        <button
                          onClick={handleBrowse}
                          disabled={isSwitching}
                          className={cn(
                            "w-full px-3 py-1.5 text-sm rounded flex items-center justify-center gap-1.5 transition-colors",
                            isSwitching
                              ? "text-muted-foreground cursor-not-allowed"
                              : "text-muted-foreground hover:bg-accent hover:text-accent-foreground cursor-pointer"
                          )}
                        >
                          <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M11 19h-6a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2h4l3 3h7a2 2 0 0 1 2 2v2.5" />
                            <path d="M15 18a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" />
                            <path d="M20.2 20.2l1.8 1.8" />
                          </svg>
                          <span>{label('welcome_browse_btn')}</span>
                        </button>
                      )}
                    </div>
                  </div>
                </div>
            </>
          )}
          </div>
        </>
      )}

      <FolderPickerModal
        open={folderPickerOpen}
        onOpenChange={setFolderPickerOpen}
        onSelect={handleFolderSelected}
        title={label('welcome_browse_btn')}
      />
    </div>
  );
}

function Switch({ checked, onCheckedChange }: { checked: boolean; onCheckedChange: () => void }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={onCheckedChange}
      className={cn(
        "relative w-9 h-5 rounded-full transition-colors flex items-center px-0.5 cursor-pointer flex-shrink-0",
        checked ? "bg-primary" : "bg-muted-foreground/40"
      )}
    >
      <span
        className={cn(
          "w-3.5 h-3.5 bg-white rounded-full shadow transition-transform",
          checked && "translate-x-[18px]"
        )}
      />
    </button>
  );
}

function Spinner({ size = 'md' }: { size?: 'sm' | 'md' }) {
  return (
    <svg className={cn("animate-spin flex-shrink-0", size === 'sm' ? "w-4 h-4" : "w-5 h-5")} viewBox="0 0 24 24" fill="none">
      <circle cx="12" cy="12" r="10" stroke="currentColor" strokeOpacity="0.3" strokeWidth="4" />
      <path d="M22 12a10 10 0 0 0-10-10" stroke="currentColor" strokeWidth="4" strokeLinecap="round" />
    </svg>
  );
}
