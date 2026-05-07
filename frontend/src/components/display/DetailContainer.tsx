import { cn } from '@/lib/utils';

interface DetailContainerProps {
  children: React.ReactNode;
  className?: string;
}

export const CARD_WIDTH = 'w-[40rem] max-w-full';
export const FULL_WIDTH_CARD = 'col-span-full justify-self-center w-full max-w-[calc(40rem*2+1.25rem)]';

export default function DetailContainer({ children, className }: DetailContainerProps) {
  return (
    <div className="h-full overflow-auto">
      <div
        data-detail-container
        className={cn(
          'px-5 py-5 grid grid-cols-[repeat(auto-fill,minmax(0,40rem))] gap-5 justify-center content-start',
          className
        )}
      >
        {children}
      </div>
    </div>
  );
}
