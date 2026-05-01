import { cn } from '@/lib/utils';

interface DetailContainerProps {
  children: React.ReactNode;
  /** Additional class names for the inner container */
  className?: string;
}

export default function DetailContainer({ children, className }: DetailContainerProps) {
  return (
    <div className="h-full overflow-auto">
      <div
        data-detail-container
        className={cn(
          "w-full max-w-300 mx-auto px-5 py-5",
          "lg:px-0",
          className
        )}
      >
        {children}
      </div>
    </div>
  );
}
