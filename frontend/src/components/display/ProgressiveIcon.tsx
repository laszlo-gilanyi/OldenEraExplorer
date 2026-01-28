import { useState, useCallback, useMemo } from 'react';
import type { CSSProperties } from 'react';
import { useImageStore, normalizeImagePath, globalImageCache } from '@/stores/imageStore';
import { cn } from '@/lib/utils';

interface ProgressiveIconProps {
  iconPath: string | null | undefined;
  alt?: string;
  size?: number;
  className?: string;
  style?: CSSProperties;
  imgClassName?: string;
}

export default function ProgressiveIcon({
  iconPath,
  alt = 'Icon',
  size = 48,
  className,
  style,
  imgClassName,
}: ProgressiveIconProps) {
  const version = useImageStore((s) => s.version);
  const retryCount = useImageStore((s) => s.retryCount);
  const markLoaded = useImageStore((s) => s.markLoaded);

  const normalizedPath = useMemo(() => normalizeImagePath(iconPath), [iconPath]);
  const wasCached = normalizedPath ? globalImageCache.has(normalizedPath) : false;

  const [loaded, setLoaded] = useState(wasCached);
  const [error, setError] = useState(false);

  const [trackedVersion, setTrackedVersion] = useState(version);
  if (version !== trackedVersion) {
    setTrackedVersion(version);
    if (!wasCached) {
      setLoaded(false);
    }
    setError(false);
  }

  const [trackedRetry, setTrackedRetry] = useState(retryCount);
  if (retryCount !== trackedRetry) {
    setTrackedRetry(retryCount);
    if (error) {
      setError(false);
    }
  }

  const [trackedIconPath, setTrackedIconPath] = useState(iconPath);
  if (iconPath !== trackedIconPath) {
    setTrackedIconPath(iconPath);
    const nowCached = normalizedPath ? globalImageCache.has(normalizedPath) : false;
    setLoaded(nowCached);
    setError(false);
  }

  const handleLoad = useCallback(() => {
    setLoaded(true);
    setError(false);
    markLoaded(iconPath);
    if (normalizedPath) {
      globalImageCache.add(normalizedPath);
    }
  }, [iconPath, normalizedPath, markLoaded]);

  const handleError = useCallback(() => {
    setError(true);
  }, []);

  if (!iconPath) {
    return <Placeholder size={size} className={className} style={style} />;
  }

  const imageUrl = `/api/assets/png/${iconPath}?v=${version}_${retryCount}`;

  if (error) {
    return <Placeholder size={size} className={className} style={style} />;
  }

  if (wasCached) {
    return (
      <div
        className={cn('relative', className)}
        style={{ width: size, height: size, ...style }}
      >
        <img
          src={imageUrl}
          alt={alt}
          onError={handleError}
          className={cn('w-full h-full object-contain', imgClassName)}
        />
      </div>
    );
  }

  return (
    <div
      className={cn('relative', className)}
      style={{ width: size, height: size, ...style }}
    >
      {!loaded && <Placeholder size={size} className="absolute inset-0" />}
      <img
        src={imageUrl}
        alt={alt}
        onLoad={handleLoad}
        onError={handleError}
        className={cn(
          'w-full h-full object-contain transition-opacity duration-300',
          loaded ? 'opacity-100' : 'opacity-0',
          imgClassName
        )}
      />
    </div>
  );
}

function Placeholder({
  size,
  className,
  style,
}: {
  size: number;
  className?: string;
  style?: CSSProperties;
}) {
  return (
    <div
      className={cn(
        'flex items-center justify-center rounded shrink-0 bg-muted',
        className
      )}
      style={{ width: size, height: size, ...style }}
    >
      <svg
        width={size * 0.5}
        height={size * 0.5}
        viewBox="0 0 24 24"
        fill="none"
        stroke="#666"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
        <circle cx="8.5" cy="8.5" r="1.5" />
        <polyline points="21 15 16 10 5 21" />
      </svg>
    </div>
  );
}
