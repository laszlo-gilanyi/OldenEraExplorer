import { useNavigate } from 'react-router-dom';
import HexagonFrame from './HexagonFrame';
import ProgressiveIcon from './ProgressiveIcon';

const SIZE_CONFIG = {
  sm: { hex: 48, width: 'w-12', amountText: 'text-xs', nameText: 'text-[10px]' },
  compact: { hex: 64, width: 'w-14', amountText: 'text-[0.85rem]', nameText: 'text-xs' },
  md: { hex: 80, width: 'w-20', amountText: 'text-[0.85rem]', nameText: 'text-xs' },
  lg: { hex: 104, width: 'w-26', amountText: 'text-[0.95rem]', nameText: 'text-sm' },
} as const;

type Size = keyof typeof SIZE_CONFIG;

interface UnitHexCardProps {
  unitId: string;
  unitName: string;
  icon: string | null;
  amount: string | number;
  size?: Size;
}

export default function UnitHexCard({
  unitId,
  unitName,
  icon,
  amount,
  size = 'md',
}: UnitHexCardProps) {
  const navigate = useNavigate();
  const config = SIZE_CONFIG[size];

  return (
    <div className={`${config.width} m-1 flex flex-col items-center`}>
      <HexagonFrame size={config.hex} className="mb-1.5">
        <ProgressiveIcon
          iconPath={icon ?? ''}
          alt={unitName}
          size={config.hex}
          imgClassName="object-cover"
        />
      </HexagonFrame>
      <div className={`${config.amountText} font-semibold text-foreground text-center`}>
        {amount}
      </div>
      <button
        onClick={() => navigate(`/units/${unitId}`)}
        className={`${config.nameText} text-semantic-gold hover:text-semantic-gold/80 text-center bg-transparent border-none p-0 cursor-pointer font-semibold leading-tight`}
      >
        {unitName}
      </button>
    </div>
  );
}
