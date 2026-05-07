import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import type { EntityReferenceDto, EntityReferencesResponse } from '@/api/types';
import { useLabels } from '@/hooks/useLabels';
import EntityChip from '@/components/display/EntityChip';
import { cn } from '@/lib/utils';

interface UsedBySectionProps {
  entityType: string;
  entityId: string;
  title?: string;  // Optional localized title that overrides the default
  className?: string;
}

export default function UsedBySection({ entityType, entityId, title, className }: UsedBySectionProps) {
  const [references, setReferences] = useState<EntityReferencesResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const navigate = useNavigate();
  const { label } = useLabels();

  useEffect(() => {
    async function fetchReferences() {
      try {
        setIsLoading(true);
        setError(null);

        const response = await fetch(`/api/references/${entityType}/${entityId}`);

        if (!response.ok) {
          throw new Error(`Failed to fetch references: ${response.statusText}`);
        }

        const data: EntityReferencesResponse = await response.json();
        setReferences(data);
      } catch (err) {
        setError(err instanceof Error ? err : new Error('Unknown error'));
      } finally {
        setIsLoading(false);
      }
    }

    fetchReferences();
  }, [entityType, entityId]);

  if (isLoading) {
    return (
      <div className="p-3 text-muted-foreground text-sm">
        {label('usedby_loading')}
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-3 text-destructive text-sm">
        {label('usedby_error', label(error.message))}
      </div>
    );
  }

  if (!references || references.referencedBy.length === 0) {
    return null;
  }

  const groupedReferences = references.referencedBy.reduce((acc, ref) => {
    const type = ref.entityType;
    if (!acc[type]) {
      acc[type] = [];
    }
    acc[type].push(ref);
    return acc;
  }, {} as Record<string, EntityReferenceDto[]>);

  const getRouteForEntityType = (type: string): string => {
    const typeMap: Record<string, string> = {
      unit: '/units',
      hero: '/heroes',
      skill: '/skills',
      ability: '/abilities',
      spell: '/spells',
      artifact: '/artifacts',
      building: '/buildings',
      subclass: '/subclasses',
      mapobject: '/map-objects',
    };
    return typeMap[type] || `/${type}s`;
  };

  const handleReferenceClick = (ref: EntityReferenceDto) => {
    const route = getRouteForEntityType(ref.entityType);
    navigate(`${route}/${ref.entityId}`);
  };

  const formatEntityType = (type: string): string => {
    return type.charAt(0).toUpperCase() + type.slice(1) + 's';
  };

  const getSectionTitle = (): string => {
    switch (entityType) {
      case 'skill':
        return label('usedby_heroes_skill');
      case 'ability':
        return label('usedby_creatures_ability');
      case 'spell':
        return label('usedby_heroes_spell');
      default:
        return label('usedby_default');
    }
  };

  const useSimplifiedDisplay = entityType === 'skill' || entityType === 'spell';
  const allReferences = references.referencedBy;

  return (
    <div className={cn("bg-card border border-border rounded-2xl p-5", className)}>
      <h3 className="m-0 mb-3 text-base font-semibold text-foreground">
        {title || getSectionTitle()}
      </h3>

      {useSimplifiedDisplay ? (
        <div className="grid grid-cols-[repeat(auto-fill,minmax(11rem,11.5rem))] gap-3">
          {allReferences.map((ref, index) => (
            <EntityChip
              key={`${ref.entityId}-${index}`}
              iconPath={ref.iconPath}
              name={ref.displayName || ref.entityId}
              onClick={() => handleReferenceClick(ref)}
            />
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {Object.entries(groupedReferences).map(([type, refs]) => (
            <div key={type}>
              <div className="text-sm font-medium text-muted-foreground mb-1.5 uppercase tracking-wider">
                {formatEntityType(type)}
              </div>
              <div className="flex flex-col gap-1">
                {refs.map((ref, index) => (
                  <button
                    key={`${ref.entityId}-${index}`}
                    onClick={() => handleReferenceClick(ref)}
                    className="flex justify-between items-center py-2 px-3 bg-muted border border-border rounded-md text-foreground text-sm cursor-pointer transition-all text-left hover:bg-border hover:border-blue-500"
                  >
                    <div className="flex-1">
                      <div className="font-medium">
                        {ref.displayName || ref.entityId}
                      </div>
                      {ref.displayName && ref.displayName !== ref.entityId && (
                        <div className="text-xs text-muted-foreground/60 font-mono mt-0.5">
                          {ref.entityId}
                        </div>
                      )}
                    </div>
                    <div className="text-xs text-muted-foreground/60 px-1.5 py-0.5 bg-card rounded">
                      {ref.propertyPath}
                    </div>
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
