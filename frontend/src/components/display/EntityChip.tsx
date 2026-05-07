import ProgressiveIcon from './ProgressiveIcon';
import { cn } from '@/lib/utils';

interface EntityChipProps {
  iconPath: string | null | undefined;
  name: string;
  onClick: () => void;
  iconScale?: number;
  className?: string;
}

export default function EntityChip({ iconPath, name, onClick, iconScale, className }: EntityChipProps) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'flex items-center gap-3 px-3 py-2 bg-muted border border-border rounded-md text-sm cursor-pointer transition-colors hover:bg-accent text-left',
        className
      )}
    >
      <div className="overflow-hidden rounded shrink-0" style={{ width: 40, height: 40 }}>
        <ProgressiveIcon
          iconPath={iconPath}
          alt={name}
          size={40}
          style={iconScale ? { transform: `scale(${iconScale})` } : undefined}
        />
      </div>
      <span className="font-semibold text-semantic-gold">{name}</span>
    </button>
  );
}
