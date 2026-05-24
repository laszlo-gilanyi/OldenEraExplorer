import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from 'react';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter.js';
import { DRACOLoader } from 'three/examples/jsm/loaders/DRACOLoader.js';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { clone as cloneSkeleton } from 'three/examples/jsm/utils/SkeletonUtils.js';
import { Line2 } from 'three/examples/jsm/lines/Line2.js';
import { LineMaterial } from 'three/examples/jsm/lines/LineMaterial.js';
import { LineGeometry } from 'three/examples/jsm/lines/LineGeometry.js';
import StatsJs from 'stats.js';
import { useViewerStore } from './viewerStore';
import { useLabels } from '@/hooks/useLabels';
import type { AnimationState, MaterialInfo, SceneNode } from './viewerStore';

export interface ModelViewerHandle {
  takeScreenshot: () => string | null;
  resetCamera: () => void;
  setCameraPreset: (preset: CameraPreset) => void;
  getCameraState: () => CameraSyncState | null;
  applyCameraState: (state: CameraSyncState) => void;
  exportGlb: () => Promise<ArrayBuffer | null>;
}

export type CameraPreset = 'front' | 'back' | 'left' | 'right' | 'top' | 'bottom' | 'isometric';

export interface CameraSyncState {
  position: [number, number, number];
  target: [number, number, number];
  zoom: number;
  // Identifies the viewer that produced this state so each viewer can ignore echoes of its own updates.
  sourceId?: string;
}

export interface TextureSlot {
  id: string;
  name: string;
  materialNames: string[];
  channel?: TextureChannel;
  baseSlotId?: string;
  optional?: boolean;
  // The GLB-embedded image name backing this slot (no extension). Undefined for optional/synthetic
  // slots that have no source texture.
  sourceTextureName?: string;
}

type TextureChannel = 'map' | 'emissiveMap';

interface TextureSlotTarget {
  materialUuid: string;
  channel: TextureChannel;
  sourceTexture: THREE.Texture;
}

interface BoneVertexMapping {
  skinnedMesh: THREE.SkinnedMesh;
  vertexIndices: number[];
  boneIndices: Set<number>;
}

interface BoneWireframeMapping {
  skinnedMesh: THREE.SkinnedMesh;
  triangleIndices: number[];
  vertexIndices: Set<number>;
}

interface ModelViewerProps {
  glbUrl: string;
  onModelLoaded?: () => void;
  statsContainer?: HTMLElement | null;
  unitScale?: number | null;
  faction?: string | null;
  // Single legacy texture override applied to the first base-color slot. Prefer customTextureUrls.
  customTextureUrl?: string | null;
  // Per-slot texture overrides keyed by TextureSlot.id (returned via onTextureSlotsReady).
  customTextureUrls?: Record<string, string | null | undefined>;
  // Fired after the model is loaded with the list of upload-targetable texture slots.
  onTextureSlotsReady?: (slots: TextureSlot[]) => void;
  // When false, the viewer does not write back to the global Zustand store (used by the secondary
  // viewer in compare mode so it does not fight the primary for materials/scene/animations state).
  syncStore?: boolean;
  // Stable identifier tagged on emitted camera state to break sync feedback loops between viewers.
  cameraSyncId?: string;
  // Inbound camera state from a peer viewer. Ignored when its sourceId matches this viewer's id.
  cameraSyncState?: CameraSyncState | null;
  onCameraSyncStateChange?: (state: CameraSyncState) => void;
}

class ThreeViewer {
  private container: HTMLElement;
  private renderer: THREE.WebGLRenderer;
  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private controls: OrbitControls;
  private pmremGenerator: THREE.PMREMGenerator;
  private neutralEnvironment: THREE.Texture | null = null;
  private gameEnvironment: THREE.Texture | null = null;
  private gameBackground: THREE.Texture | null = null;

  private content: THREE.Group | null = null;
  private platformGroup: THREE.Group | null = null;
  private platformScene: THREE.Group | null = null;
  private platformSize: THREE.Vector3 | null = null; // Cached for resetCamera in game-preview
  private platformInitialCenter: THREE.Vector3 | null = null; // Cached platform center for consistent positioning
  private defaultCameraTarget: THREE.Vector3 | null = null;
  private defaultCameraDistance: number = 5;
  private mixer: THREE.AnimationMixer | null = null;
  // Animation clips kept alongside the model so a later GLB export can include them. Cleared together
  // with the content in clear()/clearModel().
  private animationClips: THREE.AnimationClip[] = [];
  private actions: Map<string, THREE.AnimationAction> = new Map();
  private platformMixer: THREE.AnimationMixer | null = null;
  private platformActions: Map<string, THREE.AnimationAction> = new Map();

  private lights: THREE.Light[] = [];
  private gridHelper: THREE.GridHelper | null = null;
  private axesHelper: THREE.Group | null = null;
  private skeletonHelper: THREE.SkeletonHelper | null = null;
  private stats: StatsJs | null = null;
  private statsContainer: HTMLElement | null = null;

  private boundingBoxHelpers: Map<string, THREE.BoxHelper> = new Map();
  private boneBoundingBoxHelpers: Map<string, { helper: THREE.LineSegments; mapping: BoneVertexMapping }> = new Map();
  private boneWireframeHelpers: Map<string, { helper: THREE.LineSegments; mapping: BoneWireframeMapping; positions: Float32Array }> = new Map();

  private animationFrameId: number | null = null;
  private prevTime = 0;
  private disposed = false;
  private resizeObserver: ResizeObserver | null = null;
  private skyBackgroundImage: HTMLImageElement | null = null;
  private skyBackgroundPosition = 'center center';

  private materialRegistry: Map<string, THREE.Material> = new Map();
  // Pre-computed targets per slot id so user texture uploads can be routed to the right material+channel.
  private textureSlotTargets: Map<string, TextureSlotTarget[]> = new Map();
  // Cache of loaded user textures, keyed by slot id. Surviving across compare toggles means the
  // primary canvas does not need to reload from blob URLs every time the user enters or exits
  // compare with the same custom textures already in place.
  private customTexturePool: Map<string, { url: string; texture: THREE.Texture }> = new Map();
  // Race guard: a stale TextureLoader resolution from a previous invocation must not overwrite a newer one.
  private customTextureLoadVersion = 0;
  private originalMaterialStates: Map<
    string,
    {
      map: THREE.Texture | null;
      emissiveMap: THREE.Texture | null;
      emissive: THREE.Color | null;
      emissiveIntensity: number | null;
      alphaTest: number;
      transparent: boolean;
      depthWrite: boolean;
      side: THREE.Side;
    }
  > = new Map();
  private backgroundColor = new THREE.Color('#191919');

  constructor(container: HTMLElement, statsContainer?: HTMLElement | null) {
    this.container = container;
    this.statsContainer = statsContainer || null;

    this.scene = new THREE.Scene();
    this.scene.background = this.backgroundColor;

    const aspect = container.clientWidth / container.clientHeight;
    this.camera = new THREE.PerspectiveCamera(60, aspect, 0.01, 1000);
    this.camera.position.set(0, 0, 5);
    this.scene.add(this.camera);

    this.renderer = new THREE.WebGLRenderer({
      antialias: true,
      preserveDrawingBuffer: true,
      alpha: true,
    });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    this.renderer.toneMapping = THREE.LinearToneMapping;
    this.renderer.toneMappingExposure = 1;
    // Critical: Set output color space for correct sRGB display (Three.js r152+)
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    container.appendChild(this.renderer.domElement);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.screenSpacePanning = true;

    this.pmremGenerator = new THREE.PMREMGenerator(this.renderer);
    this.pmremGenerator.compileEquirectangularShader();

    const roomEnv = new RoomEnvironment();
    this.neutralEnvironment = this.pmremGenerator.fromScene(roomEnv).texture;
    roomEnv.dispose();
    this.scene.environment = this.neutralEnvironment;

    this.platformGroup = new THREE.Group();
    this.scene.add(this.platformGroup);

    this.resizeObserver = new ResizeObserver(this.handleResize);
    this.resizeObserver.observe(container);

    this.animate(0);
  }

  private handleResize = () => {
    if (this.disposed) return;

    const width = this.container.clientWidth;
    const height = this.container.clientHeight;

    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height);

    if (this.axesHelper) {
      this.axesHelper.traverse((child) => {
        if (child instanceof Line2) {
          (child.material as LineMaterial).resolution.set(width, height);
        }
      });
    }
  };

  private animate = (time: number) => {
    if (this.disposed) return;

    this.animationFrameId = requestAnimationFrame(this.animate);

    const dt = (time - this.prevTime) / 1000;
    this.prevTime = time;

    this.controls.update();

    if (this.mixer) {
      this.mixer.update(dt);
    }
    if (this.platformMixer) {
      this.platformMixer.update(dt);
    }

    if (this.stats) {
      this.stats.update();
    }

    this.boundingBoxHelpers.forEach((helper) => {
      helper.update();
    });

    this.updateBoneBoundingBoxHelpers();
    this.updateBoneWireframeHelpers();

    this.renderer.render(this.scene, this.camera);
  };

  async loadModel(url: string): Promise<{ scene: THREE.Group; animations: THREE.AnimationClip[] }> {
    const loader = new GLTFLoader();

    const dracoLoader = new DRACOLoader();
    dracoLoader.setDecoderPath('https://www.gstatic.com/draco/versioned/decoders/1.5.6/');
    loader.setDRACOLoader(dracoLoader);

    return new Promise((resolve, reject) => {
      loader.load(
        url,
        (gltf) => {
          const scene = gltf.scene || gltf.scenes[0];
          const clips = gltf.animations || [];

          if (!scene) {
            reject(new Error('Model contains no scene'));
            return;
          }

          const clonedScene = cloneSkeleton(scene) as THREE.Group;
          clonedScene.updateMatrixWorld(true);

          resolve({ scene: clonedScene, animations: clips });
        },
        undefined,
        (error) => {
          reject(error);
        }
      );
    });
  }

  setContent(
    modelScene: THREE.Group,
    clips: THREE.AnimationClip[],
    onAnimationsReady?: (animations: AnimationState[]) => void,
    skipCameraSetup = false
  ) {
    if (skipCameraSetup) {
      this.clearModel();
    } else {
      this.clear();
    }

    this.content = modelScene;

    modelScene.updateMatrixWorld(true);

    // Setup animation mixer FIRST so we can tick it before computing bounding box
    // This ensures skinned meshes are in their idle pose, not bind pose
    let animationStates: AnimationState[] = [];
    if (clips.length > 0) {
      this.mixer = new THREE.AnimationMixer(modelScene);
      this.animationClips = clips;

      // Find default animation (prefer "idle")
      const lowerNames = clips.map((clip) => clip.name.toLowerCase());
      let defaultIndex = lowerNames.findIndex((name) => name === 'idle');
      if (defaultIndex < 0) {
        defaultIndex = lowerNames.findIndex(
          (name) => name.startsWith('idle') && !name.includes('rare')
        );
      }
      if (defaultIndex < 0) {
        defaultIndex = lowerNames.findIndex((name) => name.includes('idle'));
      }
      if (defaultIndex < 0) {
        defaultIndex = 0; // Fallback to first clip
      }

      // Create animation states
      animationStates = clips.map((clip, index) => ({
        name: clip.name,
        clip: clip.uuid,
        playing: index === defaultIndex,
        time: 0,
        duration: clip.duration,
      }));

      // Setup actions
      clips.forEach((clip, index) => {
        const action = this.mixer!.clipAction(clip);
        this.actions.set(clip.name, action);

        if (index === defaultIndex) {
          action.play();
        }
      });

      // Tick the mixer to apply the idle animation pose before computing bounding box
      this.mixer.update(0);
      modelScene.updateMatrixWorld(true);
    }

    const box = this.computeSkinnedBoundingBox(modelScene);
    const modelSize = box.getSize(new THREE.Vector3());

    console.log(`[setContent] model="${modelScene.name}" bbox.min.y=${box.min.y.toFixed(6)} size=[${modelSize.x.toFixed(4)}, ${modelSize.y.toFixed(4)}, ${modelSize.z.toFixed(4)}]`);

    if (!skipCameraSetup) {
      this.controls.reset();

      const size = modelSize.length();
      this.controls.maxDistance = size * 10;
      this.camera.near = size / 100;
      this.camera.far = size * 100;
      this.camera.updateProjectionMatrix();

      const targetY = modelSize.y / 2 + size * 0.15;
      const distance = Math.sqrt(0.49 + 0.04 + 0.49) * size * 1.2;

      this.defaultCameraTarget = new THREE.Vector3(0, targetY, 0);
      this.defaultCameraDistance = distance;

      this.controls.target.copy(this.defaultCameraTarget);
      this.camera.position.set(0, targetY, distance);
      this.camera.lookAt(0, targetY, 0);
      this.controls.update();
      this.setCameraPreset('isometric');

      console.log(`[Camera] model="${modelScene.name}" size=${size.toFixed(4)} near=${(size/100).toFixed(6)} far=${(size*100).toFixed(2)}`);

      this.controls.saveState();

      this.updateGridSize();
    }

    this.scene.add(modelScene);

    onAnimationsReady?.(animationStates);

    const materials: MaterialInfo[] = [];
    const textureSlots: TextureSlot[] = [];
    const seenMaterials = new Set<string>();
    // Group materials that share a source texture under a single slot so users only upload once.
    const baseSlotByTextureUuid = new Map<string, TextureSlot>();
    const emissionSlotByBaseSlotId = new Map<string, TextureSlot>();
    let emissionSlotCount = 0;

    this.textureSlotTargets.clear();

    const addTextureSlotTarget = (
      slot: TextureSlot,
      material: THREE.Material,
      channel: TextureChannel,
      sourceTexture: THREE.Texture,
    ) => {
      const materialName = material.name?.trim() || 'Unnamed Material';
      if (!slot.materialNames.includes(materialName)) {
        slot.materialNames.push(materialName);
      }
      const currentTargets = this.textureSlotTargets.get(slot.id) ?? [];
      currentTargets.push({ materialUuid: material.uuid, channel, sourceTexture });
      this.textureSlotTargets.set(slot.id, currentTargets);
    };

    const getBaseColorSlot = (material: THREE.Material, texture: THREE.Texture): TextureSlot => {
      let slot = baseSlotByTextureUuid.get(texture.uuid);
      if (!slot) {
        const slotNumber = textureSlots.length + 1;
        const textureName = texture.name?.trim();
        const materialName = material.name?.trim();
        slot = {
          id: `baseColor:${baseSlotByTextureUuid.size}`,
          name: textureName
            ? `${textureName} Diffuse`
            : materialName
              ? `${materialName} Diffuse`
              : `Diffuse ${slotNumber}`,
          materialNames: [],
          channel: 'map',
          sourceTextureName: textureName || undefined,
        };
        baseSlotByTextureUuid.set(texture.uuid, slot);
        textureSlots.push(slot);
      }
      return slot;
    };

    // Optional emission slots are pre-registered as targets but not surfaced in textureSlots.
    // The UI synthesizes matching virtual slots with the same id pattern when the user toggles them on.
    const getOptionalEmissionSlot = (baseSlot: TextureSlot): TextureSlot => {
      const baseName = baseSlot.name.replace(/\s*Diffuse$/i, '').trim();
      return {
        id: `optionalEmissive:${baseSlot.id}`,
        name: baseName ? `${baseName} Emission` : 'Emission',
        materialNames: [...baseSlot.materialNames],
        channel: 'emissiveMap',
        baseSlotId: baseSlot.id,
        optional: true,
      };
    };

    const getEmissionSlot = (baseSlot: TextureSlot, emissiveTexture: THREE.Texture): TextureSlot => {
      let slot = emissionSlotByBaseSlotId.get(baseSlot.id);
      if (!slot) {
        const baseName = baseSlot.name.replace(/\s*Diffuse$/i, '').trim();
        const textureName = emissiveTexture.name?.trim();
        slot = {
          id: `emissive:${emissionSlotCount++}`,
          name: baseName ? `${baseName} Emission` : 'Emission',
          materialNames: [],
          channel: 'emissiveMap',
          baseSlotId: baseSlot.id,
          sourceTextureName: textureName || undefined,
        };
        emissionSlotByBaseSlotId.set(baseSlot.id, slot);
        textureSlots.push(slot);
      }
      return slot;
    };

    modelScene.traverse((obj) => {
      if (obj instanceof THREE.Mesh) {
        const mats = Array.isArray(obj.material) ? obj.material : [obj.material];

        mats.forEach((mat) => {
          this.materialRegistry.set(mat.uuid, mat);

          if (this.hasTextureMap(mat) && mat.map) {
            const baseSlot = getBaseColorSlot(mat, mat.map);
            addTextureSlotTarget(baseSlot, mat, 'map', mat.map);
            // Pre-register an optional emission target on the base texture so a user-supplied glow map
            // can be applied even to materials that ship with no emissiveMap.
            addTextureSlotTarget(getOptionalEmissionSlot(baseSlot), mat, 'emissiveMap', mat.map);

            if (this.hasEmissiveTextureMap(mat) && mat.emissiveMap) {
              const emissionSlot = getEmissionSlot(baseSlot, mat.emissiveMap);
              addTextureSlotTarget(emissionSlot, mat, 'emissiveMap', mat.emissiveMap);
            }
          } else if (this.hasEmissiveTextureMap(mat) && mat.emissiveMap) {
            const slotNumber = textureSlots.length + 1;
            const textureName = mat.emissiveMap.name?.trim();
            const materialName = mat.name?.trim();
            const slot: TextureSlot = {
              id: `emissive:${emissionSlotCount++}`,
              name: textureName
                ? `${textureName} Emission`
                : materialName
                  ? `${materialName} Emission`
                  : `Emission ${slotNumber}`,
              materialNames: [],
              channel: 'emissiveMap',
              sourceTextureName: textureName || undefined,
            };
            textureSlots.push(slot);
            addTextureSlotTarget(slot, mat, 'emissiveMap', mat.emissiveMap);
          }

          if (!seenMaterials.has(mat.uuid)) {
            seenMaterials.add(mat.uuid);

            let color = '#ffffff';
            let metalness = 0;
            let roughness = 1;

            if (mat instanceof THREE.MeshStandardMaterial || mat instanceof THREE.MeshPhysicalMaterial) {
              color = '#' + mat.color.getHexString();
              metalness = mat.metalness;
              roughness = mat.roughness;
            } else if ('color' in mat && mat.color instanceof THREE.Color) {
              color = '#' + mat.color.getHexString();
            }

            materials.push({
              uuid: mat.uuid,
              name: mat.name || 'Unnamed Material',
              color,
              metalness,
              roughness,
              wireframe: mat.wireframe || false,
              doubleSided: mat.side === THREE.DoubleSide,
            });
          }
        });
      }
    });

    return { materials, sceneGraph: this.buildSceneGraph(modelScene), textureSlots };
  }

  private buildSceneGraph(obj: THREE.Object3D): SceneNode {
    const node: SceneNode = {
      uuid: obj.uuid,
      name: obj.name || obj.type,
      type: obj.type,
      children: [],
      visible: obj.visible,
    };

    if (obj instanceof THREE.Mesh) {
      const geometry = obj.geometry;
      if (geometry) {
        const posAttr = geometry.getAttribute('position');
        node.vertexCount = posAttr ? posAttr.count : 0;
        node.faceCount = geometry.index ? geometry.index.count / 3 : (posAttr ? posAttr.count / 3 : 0);
      }
      const mat = obj.material;
      if (Array.isArray(mat)) {
        node.materialName = mat.map((m) => m.name || 'Unnamed').join(', ');
      } else {
        node.materialName = mat.name || 'Unnamed';
      }
    }

    obj.children.forEach((child) => {
      node.children.push(this.buildSceneGraph(child));
    });

    return node;
  }

  private hasTextureMap(
    material: THREE.Material,
  ): material is THREE.Material & { map: THREE.Texture | null } {
    return 'map' in material;
  }

  private hasEmissiveTextureMap(
    material: THREE.Material,
  ): material is THREE.Material & {
    emissiveMap: THREE.Texture | null;
    emissive?: THREE.Color;
    emissiveIntensity?: number;
  } {
    return 'emissiveMap' in material;
  }

  private saveOriginalMaterialState(material: THREE.Material) {
    if (this.originalMaterialStates.has(material.uuid)) return;

    const emissiveMaterial = this.hasEmissiveTextureMap(material) ? material : null;
    const emissive =
      emissiveMaterial?.emissive instanceof THREE.Color ? emissiveMaterial.emissive.clone() : null;

    this.originalMaterialStates.set(material.uuid, {
      map: this.hasTextureMap(material) ? material.map ?? null : null,
      emissiveMap: emissiveMaterial ? emissiveMaterial.emissiveMap ?? null : null,
      emissive,
      emissiveIntensity:
        typeof emissiveMaterial?.emissiveIntensity === 'number'
          ? emissiveMaterial.emissiveIntensity
          : null,
      alphaTest: material.alphaTest,
      transparent: material.transparent,
      depthWrite: material.depthWrite,
      side: material.side,
    });
  }

  // Mirror the sampler / transform parameters of the texture this one is replacing, so the
  // uploaded image lines up under the same UVs as the original.
  private configureUploadedTexture(
    texture: THREE.Texture,
    sourceTexture: THREE.Texture,
    channel: TextureChannel,
  ) {
    texture.colorSpace =
      channel === 'emissiveMap'
        ? THREE.SRGBColorSpace
        : sourceTexture.colorSpace || THREE.SRGBColorSpace;
    texture.flipY = true;

    texture.wrapS = sourceTexture.wrapS;
    texture.wrapT = sourceTexture.wrapT;

    texture.offset.copy(sourceTexture.offset);
    texture.repeat.copy(sourceTexture.repeat);
    texture.center.copy(sourceTexture.center);
    texture.rotation = sourceTexture.rotation;

    texture.matrixAutoUpdate = sourceTexture.matrixAutoUpdate;
    if (!sourceTexture.matrixAutoUpdate) {
      texture.matrix.copy(sourceTexture.matrix);
    }

    texture.generateMipmaps = sourceTexture.generateMipmaps;
    texture.minFilter = sourceTexture.minFilter;
    texture.magFilter = sourceTexture.magFilter;
    texture.anisotropy = sourceTexture.anisotropy;

    texture.needsUpdate = true;
  }

  private applyTextureToMaterial(
    material: THREE.Material,
    channel: TextureChannel,
    texture: THREE.Texture,
  ) {
    if (channel === 'map') {
      if (!this.hasTextureMap(material)) return;
      material.map = texture;
      material.needsUpdate = true;
      return;
    }

    if (!this.hasEmissiveTextureMap(material)) return;

    material.emissiveMap = texture;

    // PBR materials only sample the emissiveMap when emissive color is non-black. Promote it to
    // white so a user-supplied glow map actually shows; the original color is restored on clear.
    if (material.emissive instanceof THREE.Color) {
      const isBlack =
        material.emissive.r === 0 && material.emissive.g === 0 && material.emissive.b === 0;
      if (isBlack) {
        material.emissive.setRGB(1, 1, 1);
      }
    }

    if (typeof material.emissiveIntensity === 'number' && material.emissiveIntensity <= 0) {
      material.emissiveIntensity = 1;
    }

    material.needsUpdate = true;
  }

  private restoreOriginalMaterialStates() {
    this.materialRegistry.forEach((material) => {
      const state = this.originalMaterialStates.get(material.uuid);
      if (!state) return;

      if (this.hasTextureMap(material)) {
        material.map = state.map;
      }

      if (this.hasEmissiveTextureMap(material)) {
        material.emissiveMap = state.emissiveMap;
        if (state.emissive && material.emissive instanceof THREE.Color) {
          material.emissive.copy(state.emissive);
        }
        if (state.emissiveIntensity !== null && typeof material.emissiveIntensity === 'number') {
          material.emissiveIntensity = state.emissiveIntensity;
        }
      }

      material.alphaTest = state.alphaTest;
      material.transparent = state.transparent;
      material.depthWrite = state.depthWrite;
      material.side = state.side;
      material.needsUpdate = true;
    });

    this.originalMaterialStates.clear();
  }

  private disposeCustomTexturePool() {
    this.customTexturePool.forEach((entry) => entry.texture.dispose());
    this.customTexturePool.clear();
  }

  async setCustomTextureUrls(urls: Record<string, string | null | undefined> = {}) {
    const version = ++this.customTextureLoadVersion;
    const entries = Object.entries(urls).filter(
      (entry): entry is [string, string] => Boolean(entry[1]),
    );

    if (entries.length === 0) {
      // Strip customs from materials but keep the pool intact. A subsequent call with the same
      // URLs (typical compare on -> compare off) can then reuse the cached THREE.Texture
      // instances instead of going through TextureLoader again.
      this.restoreOriginalMaterialStates();
      return;
    }

    // Evict cached entries whose slot has been removed since the last apply (e.g., a per-slot
    // reset). Entries whose URL changed get evicted further down before the fresh load.
    const wantedSlots = new Set(entries.map(([slotId]) => slotId));
    this.customTexturePool.forEach((entry, slotId) => {
      if (!wantedSlots.has(slotId)) {
        entry.texture.dispose();
        this.customTexturePool.delete(slotId);
      }
    });

    const loader = new THREE.TextureLoader();
    const slotEntries = await Promise.all(
      entries.map(async ([slotId, url]) => {
        const cached = this.customTexturePool.get(slotId);
        if (cached?.url === url) {
          return { slotId, url, texture: cached.texture, fromCache: true };
        }
        if (cached) cached.texture.dispose();
        const texture = await loader.loadAsync(url);
        return { slotId, url, texture, fromCache: false };
      }),
    );

    if (this.disposed || version !== this.customTextureLoadVersion) {
      // A newer call has superseded ours. Drop any textures we freshly loaded; cached textures
      // belong to the pool and stay there for the newer call to reuse.
      slotEntries.forEach((entry) => {
        if (!entry.fromCache) entry.texture.dispose();
      });
      return;
    }

    // Publish newly-loaded textures to the pool (cache hits are already there).
    slotEntries.forEach(({ slotId, url, texture, fromCache }) => {
      if (!fromCache) this.customTexturePool.set(slotId, { url, texture });
    });

    this.restoreOriginalMaterialStates();

    const configuredSlots = new Set<string>();
    slotEntries.forEach(({ slotId, texture: replacementTexture }) => {
      const targets = this.textureSlotTargets.get(slotId);
      if (!targets?.length) return;

      targets.forEach((target) => {
        const material = this.materialRegistry.get(target.materialUuid);
        if (!material) return;

        this.saveOriginalMaterialState(material);

        if (!configuredSlots.has(slotId)) {
          this.configureUploadedTexture(replacementTexture, target.sourceTexture, target.channel);
          configuredSlots.add(slotId);
        }

        this.applyTextureToMaterial(material, target.channel, replacementTexture);
      });
    });
  }

  // Back-compat shim for callers that only target the first base-color slot.
  async setCustomTextureUrl(url: string | null) {
    await this.setCustomTextureUrls(url ? { 'baseColor:0': url } : {});
  }

  private clear() {
    // Restore before disposing materials so we do not leave a now-disposed custom texture
    // referenced by the material slot on the way out.
    this.restoreOriginalMaterialStates();
    this.disposeCustomTexturePool();

    if (this.content) {
      this.scene.remove(this.content);

      // Dispose geometry and materials
      this.content.traverse((obj) => {
        if (obj instanceof THREE.Mesh) {
          if (obj.geometry) {
            obj.geometry.dispose();
          }
          const materials = Array.isArray(obj.material) ? obj.material : [obj.material];
          materials.forEach((mat) => {
            if (mat) {
              // Dispose textures
              const textureProps = [
                'map', 'lightMap', 'bumpMap', 'normalMap', 'specularMap',
                'envMap', 'alphaMap', 'aoMap', 'displacementMap',
                'emissiveMap', 'gradientMap', 'metalnessMap', 'roughnessMap'
              ] as const;

              textureProps.forEach((prop) => {
                const texture = (mat as unknown as Record<string, THREE.Texture | undefined>)[prop];
                if (texture instanceof THREE.Texture) {
                  texture.dispose();
                }
              });

              mat.dispose();
            }
          });
        }
      });

      this.content = null;
    }

    // Clear mixer
    if (this.mixer) {
      this.mixer.stopAllAction();
      this.mixer = null;
    }
    this.actions.clear();
    this.animationClips = [];

    // Clear material registry
    this.materialRegistry.clear();
    this.textureSlotTargets.clear();

    // Clear platform
    if (this.platformScene) {
      this.platformGroup?.remove(this.platformScene);
      this.platformScene = null;
    }
    this.platformSize = null;
    this.defaultCameraTarget = null;
    this.defaultCameraDistance = 5;

    // Clear Phase 2 helpers
    this.clearHelpers();
  }

  // Clear only the model content, keeping the platform intact
  // Used when switching models in Game Preview mode
  private clearModel() {
    this.restoreOriginalMaterialStates();
    this.disposeCustomTexturePool();

    // Dispose of content but keep platform
    if (this.content) {
      this.scene.remove(this.content);
      this.content.traverse((node) => {
        if (node instanceof THREE.Mesh) {
          node.geometry?.dispose();
          const materials = Array.isArray(node.material) ? node.material : [node.material];
          materials.forEach((mat) => {
            if (mat) {
              Object.values(mat).forEach((value) => {
                if (value instanceof THREE.Texture) {
                  value.dispose();
                }
              });
              mat.dispose();
            }
          });
        }
      });
      this.content = null;
    }

    // Clear mixer
    if (this.mixer) {
      this.mixer.stopAllAction();
      this.mixer = null;
    }
    this.actions.clear();
    this.animationClips = [];

    // Clear material registry
    this.materialRegistry.clear();
    this.textureSlotTargets.clear();

    // Keep platform! Don't clear platformScene, platformSize

    // Clear Phase 2 helpers
    this.clearHelpers();
  }

  // Animation control methods
  // Simplified to match donmccurdy viewer approach: play() / stop()
  syncAnimations(storeAnimations: AnimationState[], loopMode: 'once' | 'repeat' | 'pingpong') {
    storeAnimations.forEach((anim) => {
      const action = this.actions.get(anim.name);
      if (!action) return;

      // Apply loop mode
      switch (loopMode) {
        case 'once':
          action.setLoop(THREE.LoopOnce, 1);
          action.clampWhenFinished = true;
          break;
        case 'repeat':
          action.setLoop(THREE.LoopRepeat, Infinity);
          action.clampWhenFinished = false;
          break;
        case 'pingpong':
          action.setLoop(THREE.LoopPingPong, Infinity);
          action.clampWhenFinished = false;
          break;
      }

      // Simple play/stop like donmccurdy viewer
      action.setEffectiveTimeScale(1);
      if (anim.playing) {
        action.play();
      } else {
        action.stop();
      }

      // Seek support
      if (anim.playing && Number.isFinite(anim.time) && anim.duration > 0) {
        const clampedTime = Math.max(0, Math.min(anim.time, anim.duration));
        if (Math.abs(action.time - clampedTime) > 1e-3) {
          action.time = clampedTime;
        }
      }
    });
  }

  setPlaybackSpeed(speed: number) {
    if (this.mixer) {
      this.mixer.timeScale = speed;
    }
    // Also apply to platform animations
    if (this.platformMixer) {
      this.platformMixer.timeScale = speed;
    }
  }

  // Apply loop mode to platform animations (all platform animations are always playing)
  syncPlatformAnimations(loopMode: 'once' | 'repeat' | 'pingpong') {
    this.platformActions.forEach((action) => {
      switch (loopMode) {
        case 'once':
          action.setLoop(THREE.LoopOnce, 1);
          action.clampWhenFinished = true;
          break;
        case 'repeat':
          action.setLoop(THREE.LoopRepeat, Infinity);
          action.clampWhenFinished = false;
          break;
        case 'pingpong':
          action.setLoop(THREE.LoopPingPong, Infinity);
          action.clampWhenFinished = false;
          break;
      }
    });
  }

  // Display settings
  // Studio mode (donmccurdy style):
  // - Checkbox OFF (show=false): backgroundColor
  // - Checkbox ON (show=true): environment map as visible background
  // Game-preview mode:
  // - Checkbox OFF (show=false): unit_info_back.png (gameBackground)
  // - Checkbox ON (show=true): Cold Sunset Equirect.png (gameEnvironment)
  setBackground(show: boolean, color: string, displayMode: 'studio' | 'game-preview') {
    this.backgroundColor.set(color);

    if (displayMode === 'studio') {
      this.renderer.setClearAlpha(1);
      this.scene.background = show ? this.neutralEnvironment : this.backgroundColor;
    } else {
      if (show) {
        // Show equirect skybox (Cold Sunset)
        this.renderer.setClearAlpha(1);
        this.scene.background = this.gameEnvironment ?? this.backgroundColor;
      } else {
        // CSS sky background handles the visual; canvas renders transparently on top
        this.renderer.setClearAlpha(0);
        this.scene.background = null;
      }
    }
  }

  setCanvasSkyBackground(url: string | null, position = 'center center') {
    const container = this.renderer.domElement.parentElement;
    if (!container) return;
    if (url) {
      container.style.backgroundImage = `url(${url})`;
      container.style.backgroundSize = 'cover';
      container.style.backgroundPosition = position;
      this.skyBackgroundPosition = position;
      const img = new Image();
      img.onload = () => { this.skyBackgroundImage = img; };
      img.src = url;
    } else {
      container.style.backgroundImage = '';
      this.skyBackgroundImage = null;
    }
  }

  setEnvironment(displayMode: 'studio' | 'game-preview') {
    if (displayMode === 'studio') {
      this.scene.environment = this.neutralEnvironment;
    } else {
      this.scene.environment = this.gameEnvironment ?? this.neutralEnvironment;
    }
  }

  setToneMapping(mode: 'linear' | 'aces', exposure: number) {
    this.renderer.toneMapping = mode === 'aces' ? THREE.ACESFilmicToneMapping : THREE.LinearToneMapping;
    this.renderer.toneMappingExposure = Math.pow(2, exposure);
  }

  setPixelRatioLimit(limit: number) {
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, limit));
  }

  // Lights
  updateLights(
    usePunctual: boolean,
    ambientIntensity: number,
    ambientColor: string,
    directionalIntensity: number,
    directionalColor: string,
    // Spherical angles from unit_hire_preview_lighting.asset
    zenithDeg = 59.5,
    azimuthDeg = 322
  ) {
    // Remove existing lights
    this.lights.forEach((light) => {
      light.parent?.remove(light);
    });
    this.lights = [];

    if (!usePunctual) return;

    const ambient = new THREE.AmbientLight(ambientColor, ambientIntensity);
    ambient.name = 'ambient_light';
    this.scene.add(ambient);
    this.lights.push(ambient);

    const directional = new THREE.DirectionalLight(directionalColor, directionalIntensity);
    const zenithRad = (zenithDeg * Math.PI) / 180;
    const azimuthRad = (azimuthDeg * Math.PI) / 180;
    directional.position.set(
      Math.sin(zenithRad) * Math.sin(azimuthRad),
      Math.cos(zenithRad),
      Math.sin(zenithRad) * Math.cos(azimuthRad)
    );
    directional.name = 'main_light';
    this.scene.add(directional);
    this.lights.push(directional);
  }

  // Update grid size based on current camera distance
  private updateGridSize() {
    if (!this.gridHelper) return;

    const gridSize = this.defaultCameraDistance * 3;

    // Recreate grid with new size
    this.scene.remove(this.gridHelper);
    this.gridHelper.geometry.dispose();
    (this.gridHelper.material as THREE.Material).dispose();

    this.gridHelper = new THREE.GridHelper(gridSize, 10);
    (this.gridHelper.material as THREE.Material).polygonOffset = true;
    (this.gridHelper.material as THREE.Material).polygonOffsetFactor = 1;
    (this.gridHelper.material as THREE.Material).polygonOffsetUnits = 1;
    this.scene.add(this.gridHelper);

    // Recreate axes with length proportional to camera distance
    if (this.axesHelper) {
      this.scene.remove(this.axesHelper);
      this.axesHelper.traverse((child) => {
        if (child instanceof Line2) {
          child.geometry.dispose();
          (child.material as LineMaterial).dispose();
        }
      });

      this.axesHelper = new THREE.Group();
      this.axesHelper.renderOrder = 1;

      const axisLength = this.defaultCameraDistance * 0.5;
      const lineWidth = 5;

      const createAxis = (positions: number[], color: number) => {
        const geom = new LineGeometry();
        geom.setPositions(positions);
        const mat = new LineMaterial({
          color,
          linewidth: lineWidth,
          resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
          polygonOffset: true,
          polygonOffsetFactor: -1,
          polygonOffsetUnits: -1,
        });
        const line = new Line2(geom, mat);
        line.computeLineDistances();
        return line;
      };

      this.axesHelper.add(createAxis([0, 0, 0, axisLength, 0, 0], 0xff0000));
      this.axesHelper.add(createAxis([0, 0, 0, 0, axisLength, 0], 0x00ff00));
      this.axesHelper.add(createAxis([0, 0, 0, 0, 0, axisLength], 0x0000ff));

      this.scene.add(this.axesHelper);
    }
  }

  // Toggle grid and axes helpers
  setGrid(show: boolean) {
    if (show && !this.gridHelper) {
      // Scale grid size based on camera distance for consistent visual appearance
      const gridSize = this.defaultCameraDistance * 3;

      // Create grid with calculated size
      this.gridHelper = new THREE.GridHelper(gridSize, 10);
      // Push grid back in depth buffer to avoid z-fighting with axes
      (this.gridHelper.material as THREE.Material).polygonOffset = true;
      (this.gridHelper.material as THREE.Material).polygonOffsetFactor = 1;
      (this.gridHelper.material as THREE.Material).polygonOffsetUnits = 1;
      this.scene.add(this.gridHelper);

      // Create thick axes using Line2 (works on all platforms, unlike linewidth)
      this.axesHelper = new THREE.Group();
      this.axesHelper.renderOrder = 1; // Render after grid

      const axisLength = this.defaultCameraDistance * 0.5;
      const lineWidth = 5;

      // X axis (red)
      const xGeom = new LineGeometry();
      xGeom.setPositions([0, 0, 0, axisLength, 0, 0]);
      const xMat = new LineMaterial({
        color: 0xff0000,
        linewidth: lineWidth,
        resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
        polygonOffset: true,
        polygonOffsetFactor: -1,
        polygonOffsetUnits: -1,
      });
      const xLine = new Line2(xGeom, xMat);
      xLine.computeLineDistances();
      this.axesHelper.add(xLine);

      // Y axis (green)
      const yGeom = new LineGeometry();
      yGeom.setPositions([0, 0, 0, 0, axisLength, 0]);
      const yMat = new LineMaterial({
        color: 0x00ff00,
        linewidth: lineWidth,
        resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
        polygonOffset: true,
        polygonOffsetFactor: -1,
        polygonOffsetUnits: -1,
      });
      const yLine = new Line2(yGeom, yMat);
      yLine.computeLineDistances();
      this.axesHelper.add(yLine);

      // Z axis (blue)
      const zGeom = new LineGeometry();
      zGeom.setPositions([0, 0, 0, 0, 0, axisLength]);
      const zMat = new LineMaterial({
        color: 0x0000ff,
        linewidth: lineWidth,
        resolution: new THREE.Vector2(this.container.clientWidth, this.container.clientHeight),
        polygonOffset: true,
        polygonOffsetFactor: -1,
        polygonOffsetUnits: -1,
      });
      const zLine = new Line2(zGeom, zMat);
      zLine.computeLineDistances();
      this.axesHelper.add(zLine);

      this.scene.add(this.axesHelper);
    } else if (!show && this.gridHelper) {
      // Remove grid
      this.scene.remove(this.gridHelper);
      this.gridHelper.geometry.dispose();
      (this.gridHelper.material as THREE.Material).dispose();
      this.gridHelper = null;

      // Remove axes
      if (this.axesHelper) {
        this.scene.remove(this.axesHelper);
        // Dispose Line2 children
        this.axesHelper.traverse((child) => {
          if (child instanceof Line2) {
            child.geometry.dispose();
            (child.material as LineMaterial).dispose();
          }
        });
        this.axesHelper = null;
      }
    }
  }

  setSkeleton(show: boolean) {
    // Remove existing helper
    if (this.skeletonHelper) {
      this.scene.remove(this.skeletonHelper);
      this.skeletonHelper.geometry.dispose();
      (this.skeletonHelper.material as THREE.Material).dispose();
      this.skeletonHelper = null;
    }

    if (!show) return;

    // Find skinned mesh in model and platform
    const skinnedMeshes: THREE.SkinnedMesh[] = [];
    if (this.content) {
      this.content.traverse((child) => {
        if (child instanceof THREE.SkinnedMesh) {
          skinnedMeshes.push(child);
        }
      });
    }
    if (this.platformScene) {
      this.platformScene.traverse((child) => {
        if (child instanceof THREE.SkinnedMesh) {
          skinnedMeshes.push(child);
        }
      });
    }

    const skinnedMesh = skinnedMeshes[0];
    if (!skinnedMesh || !skinnedMesh.skeleton) return;

    // Find root bone
    let rootBone: THREE.Bone | null = null;
    for (const bone of skinnedMesh.skeleton.bones) {
      if (!bone.parent || !(bone.parent instanceof THREE.Bone)) {
        rootBone = bone;
        break;
      }
    }

    if (rootBone) {
      this.skeletonHelper = new THREE.SkeletonHelper(rootBone);
      this.scene.add(this.skeletonHelper);
    }
  }

  setStats(show: boolean) {
    if (show && !this.stats && this.statsContainer) {
      this.stats = new StatsJs();
      this.stats.showPanel(0);
      this.stats.dom.style.position = 'absolute';
      this.stats.dom.style.top = '0';
      this.stats.dom.style.left = '0';
      this.stats.dom.style.zIndex = '100';
      this.statsContainer.appendChild(this.stats.dom);
    } else if (!show && this.stats) {
      if (this.stats.dom.parentElement) {
        this.stats.dom.parentElement.removeChild(this.stats.dom);
      }
      this.stats = null;
    }
  }

  // Controls settings
  setAutoRotate(enabled: boolean, speed: number) {
    this.controls.autoRotate = enabled;
    this.controls.autoRotateSpeed = speed;
  }

  // Set orbit mode: 'turntable' locks vertical rotation (Y axis only), 'free' allows full orbit
  setOrbitMode(mode: 'turntable' | 'free') {
    if (mode === 'turntable') {
      // Lock polar angle to current value (no up/down rotation)
      const currentPolar = this.controls.getPolarAngle();
      this.controls.minPolarAngle = currentPolar;
      this.controls.maxPolarAngle = currentPolar;
    } else {
      // Free orbit - allow full vertical rotation
      this.controls.minPolarAngle = 0;
      this.controls.maxPolarAngle = Math.PI;
    }
  }

  setClipPlanes(near: number, far: number) {
    this.camera.near = near;
    this.camera.far = far;
    this.camera.updateProjectionMatrix();
  }

  // Material updates
  updateMaterials(
    materials: MaterialInfo[],
    globalWireframe: boolean,
    pointSize: number
  ) {
    // The compare-mode secondary viewer loads its own copy of the GLB so its material UUIDs differ
    // from the primary's - fall back to matching by name so store-level edits propagate to both viewers.
    this.materialRegistry.forEach((material, uuid) => {
      const info =
        materials.find((m) => m.uuid === uuid) ??
        materials.find((m) => m.name === material.name);
      if (!info) return;

      if ('wireframe' in material) {
        material.wireframe = globalWireframe || info.wireframe;
      }

      if (material instanceof THREE.PointsMaterial) {
        material.size = pointSize;
      }

      const matWithColor = material as unknown as { color?: THREE.Color };
      if (matWithColor.color instanceof THREE.Color) {
        matWithColor.color.set(info.color);
      }

      if (material instanceof THREE.MeshStandardMaterial || material instanceof THREE.MeshPhysicalMaterial) {
        material.metalness = info.metalness;
        material.roughness = info.roughness;
      }

      const nextSide = info.doubleSided ? THREE.DoubleSide : THREE.FrontSide;
      if (material.side !== nextSide) {
        material.side = nextSide;
        material.needsUpdate = true;
      }
    });

    // Apply wireframe to platform materials
    if (this.platformScene) {
      this.platformScene.traverse((obj) => {
        if (obj instanceof THREE.Mesh) {
          const mats = Array.isArray(obj.material) ? obj.material : [obj.material];
          mats.forEach((mat) => {
            if ('wireframe' in mat) {
              mat.wireframe = globalWireframe;
            }
          });
        }
      });
    }
  }

  // Visibility
  updateVisibility(hiddenNodes: Set<string>, soloNode: string | null) {
    // Apply hidden nodes helper
    const applyHidden = (object: THREE.Object3D, parentHidden: boolean) => {
      const isHidden = parentHidden || hiddenNodes.has(object.uuid);
      object.visible = !isHidden;
      object.children.forEach((child) => applyHidden(child, isHidden));
    };

    if (soloNode) {
      // Hide everything first
      if (this.content) {
        this.content.traverse((obj) => {
          obj.visible = false;
        });
      }
      if (this.platformScene) {
        this.platformScene.traverse((obj) => {
          obj.visible = false;
        });
      }

      // Show solo object and ancestors/descendants (check both model and platform)
      const soloObject = this.content?.getObjectByProperty('uuid', soloNode)
        || this.platformScene?.getObjectByProperty('uuid', soloNode);
      if (soloObject) {
        let current: THREE.Object3D | null = soloObject;
        while (current) {
          current.visible = true;
          current = current.parent;
        }
        soloObject.traverse((child) => {
          child.visible = true;
        });
      }
      return;
    }

    // Apply hidden nodes to model
    if (this.content) {
      applyHidden(this.content, false);
    }

    // Apply hidden nodes to platform (independent of platformGroup visibility)
    if (this.platformScene) {
      applyHidden(this.platformScene, false);
    }
  }

  // Phase 2: BoundingBox and BoneWireframe Helpers

  // Sync bounding box helpers with the set of node UUIDs from the store
  syncBoundingBoxHelpers(nodeUuids: Set<string>) {
    // Remove helpers for nodes no longer in the set
    this.boundingBoxHelpers.forEach((helper, uuid) => {
      if (!nodeUuids.has(uuid)) {
        this.scene.remove(helper);
        helper.geometry.dispose();
        (helper.material as THREE.Material).dispose();
        this.boundingBoxHelpers.delete(uuid);
      }
    });

    // Remove bone bounding box helpers for nodes no longer in the set
    this.boneBoundingBoxHelpers.forEach((entry, uuid) => {
      if (!nodeUuids.has(uuid)) {
        this.scene.remove(entry.helper);
        entry.helper.geometry.dispose();
        (entry.helper.material as THREE.Material).dispose();
        this.boneBoundingBoxHelpers.delete(uuid);
      }
    });

    // Add helpers for new nodes
    nodeUuids.forEach((uuid) => {
      if (this.boundingBoxHelpers.has(uuid) || this.boneBoundingBoxHelpers.has(uuid)) {
        return; // Already have a helper for this node
      }

      // Find the target object
      const target = this.findObjectByUuid(uuid);
      if (!target) return;

      // Check if it's a Bone - use BoneBoundingBoxHelper
      if (target instanceof THREE.Bone) {
        this.createBoneBoundingBoxHelper(uuid, target);
      } else {
        // Regular object - use BoxHelper
        const helper = new THREE.BoxHelper(target, '#ffff00');
        this.scene.add(helper);
        this.boundingBoxHelpers.set(uuid, helper);
      }
    });
  }

  // Find an object by UUID in model and platform scenes
  private findObjectByUuid(uuid: string): THREE.Object3D | null {
    if (this.content) {
      const found = this.content.getObjectByProperty('uuid', uuid);
      if (found) return found;
    }
    if (this.platformScene) {
      const found = this.platformScene.getObjectByProperty('uuid', uuid);
      if (found) return found;
    }
    return null;
  }

  // Create a BoneBoundingBoxHelper for a bone
  private createBoneBoundingBoxHelper(uuid: string, bone: THREE.Bone) {
    const searchScene = this.content || this.scene;

    // Find the SkinnedMesh that uses this bone
    const skinnedMesh = this.findSkinnedMeshForBone(searchScene, bone);
    if (!skinnedMesh || !skinnedMesh.skeleton) return;

    // Collect bone indices (this bone + descendants)
    const boneIndices = this.collectBoneIndices(bone, skinnedMesh.skeleton);

    // Find influenced vertices
    const vertexIndices = this.findInfluencedVertices(skinnedMesh, boneIndices);
    if (vertexIndices.length === 0) return;

    const mapping: BoneVertexMapping = {
      skinnedMesh,
      vertexIndices,
      boneIndices,
    };

    // Create box wireframe
    const boxGeometry = new THREE.BoxGeometry(1, 1, 1);
    const edgesGeometry = new THREE.EdgesGeometry(boxGeometry);
    const material = new THREE.LineBasicMaterial({ color: '#00ffff' });
    const helper = new THREE.LineSegments(edgesGeometry, material);
    boxGeometry.dispose();

    this.scene.add(helper);
    this.boneBoundingBoxHelpers.set(uuid, { helper, mapping });
  }

  // Update bone bounding box helpers (called in animate loop)
  private updateBoneBoundingBoxHelpers() {
    const box = new THREE.Box3();
    const positionVec = new THREE.Vector3();

    this.boneBoundingBoxHelpers.forEach(({ helper, mapping }) => {
      const { skinnedMesh, vertexIndices } = mapping;

      // Reset box
      box.makeEmpty();

      // Compute bounding box from influenced vertices (in world space)
      for (const idx of vertexIndices) {
        skinnedMesh.getVertexPosition(idx, positionVec);
        positionVec.applyMatrix4(skinnedMesh.matrixWorld);
        box.expandByPoint(positionVec);
      }

      if (box.isEmpty()) return;

      // Update helper transform
      const center = box.getCenter(new THREE.Vector3());
      const size = box.getSize(new THREE.Vector3());

      helper.position.copy(center);
      helper.scale.copy(size);
    });
  }

  // Sync bone wireframe helpers with the set of node UUIDs from the store
  syncWireframeHelpers(nodeUuids: Set<string>) {
    // Remove helpers for nodes no longer in the set
    this.boneWireframeHelpers.forEach((entry, uuid) => {
      if (!nodeUuids.has(uuid)) {
        this.scene.remove(entry.helper);
        entry.helper.geometry.dispose();
        (entry.helper.material as THREE.Material).dispose();
        this.boneWireframeHelpers.delete(uuid);
      }
    });

    // Add helpers for new nodes (only for Bones)
    nodeUuids.forEach((uuid) => {
      if (this.boneWireframeHelpers.has(uuid)) return;

      const target = this.findObjectByUuid(uuid);
      if (!target || !(target instanceof THREE.Bone)) return;

      this.createBoneWireframeHelper(uuid, target);
    });
  }

  // Create a BoneWireframeHelper for a bone
  private createBoneWireframeHelper(uuid: string, bone: THREE.Bone) {
    const searchScene = this.content || this.scene;

    // Find the SkinnedMesh that uses this bone
    const skinnedMesh = this.findSkinnedMeshForBone(searchScene, bone);
    if (!skinnedMesh || !skinnedMesh.skeleton) return;

    // Collect bone indices
    const boneIndices = this.collectBoneIndices(bone, skinnedMesh.skeleton);

    // Find influenced vertices
    const vertexIndices = this.findInfluencedVerticesSet(skinnedMesh, boneIndices);
    if (vertexIndices.size === 0) return;

    // Find influenced triangles
    const triangleIndices = this.findInfluencedTriangles(skinnedMesh.geometry, vertexIndices);
    if (triangleIndices.length === 0) return;

    const mapping: BoneWireframeMapping = {
      skinnedMesh,
      triangleIndices,
      vertexIndices,
    };

    // Each triangle has 3 edges, each edge has 2 points, each point has 3 components
    const edgeCount = triangleIndices.length * 3;
    const positions = new Float32Array(edgeCount * 2 * 3);

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));

    const material = new THREE.LineBasicMaterial({
      color: '#00ffff',
      depthTest: true,
      depthWrite: false,
      transparent: true,
      opacity: 0.8,
    });

    const helper = new THREE.LineSegments(geometry, material);
    helper.frustumCulled = false;

    this.scene.add(helper);
    this.boneWireframeHelpers.set(uuid, { helper, mapping, positions });
  }

  // Update bone wireframe helpers (called in animate loop)
  private updateBoneWireframeHelpers() {
    const tempVec = new THREE.Vector3();

    this.boneWireframeHelpers.forEach(({ helper, mapping, positions }) => {
      const { skinnedMesh, triangleIndices } = mapping;
      const geometry = skinnedMesh.geometry;
      const index = geometry.getIndex();

      let posIdx = 0;

      for (const triIdx of triangleIndices) {
        let a: number, b: number, c: number;

        if (index) {
          a = index.array[triIdx * 3];
          b = index.array[triIdx * 3 + 1];
          c = index.array[triIdx * 3 + 2];
        } else {
          a = triIdx * 3;
          b = triIdx * 3 + 1;
          c = triIdx * 3 + 2;
        }

        // Edge 1: a -> b
        skinnedMesh.getVertexPosition(a, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        skinnedMesh.getVertexPosition(b, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        // Edge 2: b -> c
        skinnedMesh.getVertexPosition(b, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        skinnedMesh.getVertexPosition(c, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        // Edge 3: c -> a
        skinnedMesh.getVertexPosition(c, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;

        skinnedMesh.getVertexPosition(a, tempVec);
        tempVec.applyMatrix4(skinnedMesh.matrixWorld);
        positions[posIdx++] = tempVec.x;
        positions[posIdx++] = tempVec.y;
        positions[posIdx++] = tempVec.z;
      }

      // Mark buffer as needing update
      const positionAttr = helper.geometry.getAttribute('position') as THREE.BufferAttribute;
      positionAttr.needsUpdate = true;
    });
  }

  // Helper: Compute bounding box using actual skinned vertex positions (not bind pose)
  // This is necessary for animated models where limbs extend beyond the bind pose bbox
  private computeSkinnedBoundingBox(object: THREE.Object3D): THREE.Box3 {
    const box = new THREE.Box3();
    const positionVec = new THREE.Vector3();

    // First pass: update all skeletons so getVertexPosition uses current pose
    object.traverse((child) => {
      if (child instanceof THREE.SkinnedMesh && child.skeleton) {
        child.skeleton.update();
      }
    });

    // Second pass: compute bounding box from vertex positions
    object.traverse((child) => {
      if (child instanceof THREE.SkinnedMesh) {
        // For skinned meshes, iterate through all vertices and get their current positions
        const geometry = child.geometry;
        const positionAttr = geometry.getAttribute('position');
        if (!positionAttr) return;

        const vertexCount = positionAttr.count;
        for (let i = 0; i < vertexCount; i++) {
          child.getVertexPosition(i, positionVec);
          positionVec.applyMatrix4(child.matrixWorld);
          box.expandByPoint(positionVec);
        }
      } else if (child instanceof THREE.Mesh) {
        // Regular mesh - use standard bounding box
        const meshBox = new THREE.Box3().setFromObject(child);
        box.union(meshBox);
      }
    });

    // Fallback to standard bbox if no meshes found
    if (box.isEmpty()) {
      box.setFromObject(object);
    }

    return box;
  }

  // Helper: Find SkinnedMesh that contains the given bone
  private findSkinnedMeshForBone(scene: THREE.Object3D, bone: THREE.Bone): THREE.SkinnedMesh | null {
    let result: THREE.SkinnedMesh | null = null;
    scene.traverse((obj) => {
      if (result) return;
      if (obj instanceof THREE.SkinnedMesh && obj.skeleton) {
        if (obj.skeleton.bones.includes(bone)) {
          result = obj;
        }
      }
    });
    return result;
  }

  // Helper: Collect all descendant bone indices (including the bone itself)
  private collectBoneIndices(bone: THREE.Bone, skeleton: THREE.Skeleton): Set<number> {
    const indices = new Set<number>();
    const boneIndex = skeleton.bones.indexOf(bone);
    if (boneIndex !== -1) {
      indices.add(boneIndex);
    }
    bone.traverse((child) => {
      if (child instanceof THREE.Bone && child !== bone) {
        const childIndex = skeleton.bones.indexOf(child);
        if (childIndex !== -1) {
          indices.add(childIndex);
        }
      }
    });
    return indices;
  }

  // Helper: Find vertices influenced by the given bone indices (returns array)
  private findInfluencedVertices(
    skinnedMesh: THREE.SkinnedMesh,
    boneIndices: Set<number>,
    minWeight: number = 0.1
  ): number[] {
    const geometry = skinnedMesh.geometry;
    const skinIndex = geometry.getAttribute('skinIndex');
    const skinWeight = geometry.getAttribute('skinWeight');

    if (!skinIndex || !skinWeight) return [];

    const vertexIndices: number[] = [];
    const vertexCount = skinIndex.count;

    for (let i = 0; i < vertexCount; i++) {
      for (let j = 0; j < 4; j++) {
        const boneIdx = skinIndex.getComponent(i, j);
        const weight = skinWeight.getComponent(i, j);

        if (boneIndices.has(boneIdx) && weight >= minWeight) {
          vertexIndices.push(i);
          break;
        }
      }
    }

    return vertexIndices;
  }

  // Helper: Find vertices influenced by the given bone indices (returns Set)
  private findInfluencedVerticesSet(
    skinnedMesh: THREE.SkinnedMesh,
    boneIndices: Set<number>,
    minWeight: number = 0.1
  ): Set<number> {
    const geometry = skinnedMesh.geometry;
    const skinIndex = geometry.getAttribute('skinIndex');
    const skinWeight = geometry.getAttribute('skinWeight');

    if (!skinIndex || !skinWeight) return new Set();

    const vertexIndices = new Set<number>();
    const vertexCount = skinIndex.count;

    for (let i = 0; i < vertexCount; i++) {
      for (let j = 0; j < 4; j++) {
        const boneIdx = skinIndex.getComponent(i, j);
        const weight = skinWeight.getComponent(i, j);

        if (boneIndices.has(boneIdx) && weight >= minWeight) {
          vertexIndices.add(i);
          break;
        }
      }
    }

    return vertexIndices;
  }

  // Helper: Find triangles where at least one vertex is influenced by the bone
  private findInfluencedTriangles(
    geometry: THREE.BufferGeometry,
    vertexIndices: Set<number>
  ): number[] {
    const index = geometry.getIndex();
    const triangleIndices: number[] = [];

    if (index) {
      const indexArray = index.array;
      for (let i = 0; i < indexArray.length; i += 3) {
        const a = indexArray[i];
        const b = indexArray[i + 1];
        const c = indexArray[i + 2];

        if (vertexIndices.has(a) || vertexIndices.has(b) || vertexIndices.has(c)) {
          triangleIndices.push(i / 3);
        }
      }
    } else {
      const positionAttr = geometry.getAttribute('position');
      const vertexCount = positionAttr.count;
      for (let i = 0; i < vertexCount; i += 3) {
        if (vertexIndices.has(i) || vertexIndices.has(i + 1) || vertexIndices.has(i + 2)) {
          triangleIndices.push(i / 3);
        }
      }
    }

    return triangleIndices;
  }

  // Clear all helpers (called when content changes)
  private clearHelpers() {
    this.boundingBoxHelpers.forEach((helper) => {
      this.scene.remove(helper);
      helper.geometry.dispose();
      (helper.material as THREE.Material).dispose();
    });
    this.boundingBoxHelpers.clear();

    this.boneBoundingBoxHelpers.forEach(({ helper }) => {
      this.scene.remove(helper);
      helper.geometry.dispose();
      (helper.material as THREE.Material).dispose();
    });
    this.boneBoundingBoxHelpers.clear();

    this.boneWireframeHelpers.forEach(({ helper }) => {
      this.scene.remove(helper);
      helper.geometry.dispose();
      (helper.material as THREE.Material).dispose();
    });
    this.boneWireframeHelpers.clear();
  }

  // Platform loading (game-preview mode) - loads GLB from backend
  async loadPlatform(url: string): Promise<{ scene: THREE.Group; sceneGraph: SceneNode }> {
    const { scene, animations } = await this.loadModel(url);

    // Clear previous platform
    if (this.platformScene) {
      this.platformGroup?.remove(this.platformScene);
    }

    // Clear previous platform mixer
    if (this.platformMixer) {
      this.platformMixer.stopAllAction();
      this.platformMixer = null;
    }
    this.platformActions.clear();

    this.platformScene = scene;
    this.platformGroup?.add(scene);

    // Setup platform animations (separate mixer from main model)
    if (animations.length > 0) {
      this.platformMixer = new THREE.AnimationMixer(scene);

      // Play all animations by default (platform usually has a single looping animation)
      animations.forEach((clip) => {
        const action = this.platformMixer!.clipAction(clip);
        this.platformActions.set(clip.name, action);
        action.play();
      });

      // Tick the mixer to apply initial pose
      this.platformMixer.update(0);
      scene.updateMatrixWorld(true);

      console.log(`[Platform] loaded ${animations.length} animation(s):`, animations.map(a => a.name).join(', '));
    }

    // Build scene graph for hierarchy panel
    const sceneGraph = this.buildSceneGraph(scene);

    return { scene, sceneGraph };
  }

  setPlatformVisible(visible: boolean) {
    if (this.platformGroup) {
      this.platformGroup.visible = visible;
    }
  }

  layoutGamePreview(unitScale: number | null) {
    if (!this.platformGroup || !this.platformScene || !this.content) return;

    // 1. Reset model scale to 1 first (so this method can be called multiple times)
    // Then apply unit scale (like the original game does)
    // The game preserves the sign of each scale component and multiplies by the config scale
    const scale = unitScale != null && unitScale > 0 ? unitScale : 1;
    this.content.scale.set(scale, scale, scale);
    this.content.updateWorldMatrix(true, true);
    console.log(`[Layout] applied unitScale=${scale} to model="${this.content.name}"`);

    // 2. Reset platform transforms WITHOUT rotation first (for consistent measurement)
    const PLATFORM_SCALE = 1.15;
    this.platformGroup.position.set(0, 0, 0);
    this.platformGroup.scale.set(PLATFORM_SCALE, PLATFORM_SCALE, PLATFORM_SCALE);
    this.platformGroup.rotation.set(0, 0, 0);
    this.platformGroup.updateWorldMatrix(true, true);

    // 3. Measure platform BEFORE rotation for consistency with setContent()
    const platformBox = this.computeSkinnedBoundingBox(this.platformGroup);
    this.platformSize = platformBox.getSize(new THREE.Vector3());

    console.log(`[Platform] size: [${this.platformSize.x.toFixed(4)}, ${this.platformSize.y.toFixed(4)}, ${this.platformSize.z.toFixed(4)}]`);

    this.platformGroup.updateWorldMatrix(true, true);

    // 4. Get rotated platform bounds for positioning
    const rotatedPlatformBox = this.computeSkinnedBoundingBox(this.platformGroup);

    if (!this.platformInitialCenter) {
      this.platformInitialCenter = rotatedPlatformBox.getCenter(new THREE.Vector3());
    }
    const platformCenter = this.platformInitialCenter;

    // 5. Raycast from above to find platform surface Y
    const raycaster = new THREE.Raycaster();
    raycaster.set(
      new THREE.Vector3(platformCenter.x, rotatedPlatformBox.max.y + 1, platformCenter.z),
      new THREE.Vector3(0, -1, 0)
    );
    const intersects = raycaster.intersectObject(this.platformGroup, true);
    const platformSurfaceY = intersects.length > 0 ? intersects[0].point.y : rotatedPlatformBox.max.y;

    // 6. Position platform so disk center is at origin and surface is at Y=0
    // These offsets correct for the model's disk center not being at its bounding box center.
    // TODO: if the platform model changes and the disk drifts, update these two values.
    const DISK_CENTER_OFFSET_X = -0.1;
    const DISK_CENTER_OFFSET_Z = 0.3;
    const offsetY = 0 - platformSurfaceY;
    this.platformGroup.position.set(
      0 - platformCenter.x + DISK_CENTER_OFFSET_X,
      offsetY,
      0 - platformCenter.z + DISK_CENTER_OFFSET_Z
    );
    this.platformGroup.updateWorldMatrix(true, true);

    this.defaultCameraTarget = null;
    this.defaultCameraDistance = 5;
    this.setupCameraForPlatform();

    console.log(`[Layout] model="${this.content.name}" platformSurfaceY=${platformSurfaceY.toFixed(4)} cameraSize=${this.platformSize.length().toFixed(4)}`);
  }

  // Game preview environment loading
  async loadGameEnvironment(url: string): Promise<void> {
    const loader = new THREE.TextureLoader();
    const texture = await loader.loadAsync(url);
    texture.mapping = THREE.EquirectangularReflectionMapping;
    texture.colorSpace = THREE.SRGBColorSpace;

    // Convert equirectangular to PMREM cubemap for IBL
    this.gameEnvironment = this.pmremGenerator.fromEquirectangular(texture).texture;
    texture.dispose();
  }

  async loadGameBackground(url: string): Promise<void> {
    const loader = new THREE.TextureLoader();
    this.gameBackground = await loader.loadAsync(url);
    this.gameBackground.colorSpace = THREE.SRGBColorSpace;
    this.gameBackground.offset.y = -0.2; // Adjust to match ingame gradient position
  }

  resetCamera() {
    if (this.platformGroup?.visible && this.platformScene && this.platformSize) {
      this.setupCameraForPlatform();
    } else {
      this.setupCameraForModel();
    }
  }

  private setupCameraForModel() {
    if (!this.defaultCameraTarget) return;

    this.controls.reset();
    this.controls.target.copy(this.defaultCameraTarget);
    this.camera.position.set(0, this.defaultCameraTarget.y, this.defaultCameraDistance);
    this.camera.lookAt(this.defaultCameraTarget);
    this.controls.update();
    this.setCameraPreset('isometric');
    this.controls.saveState();

    // Update grid size after camera setup
    this.updateGridSize();
  }

  private setupCameraForPlatform() {
    if (!this.platformSize) return;

    if (!this.defaultCameraTarget) {
      const size = this.platformSize.length();
      this.controls.maxDistance = size * 10;
      this.camera.near = size / 100;
      this.camera.far = size * 100;
      this.camera.updateProjectionMatrix();

      const targetY = this.platformSize.y / 2;
      const lookAtY = targetY + size / 8.0;
      const distance = size * 0.7;

      this.defaultCameraTarget = new THREE.Vector3(0, lookAtY, 0);
      this.defaultCameraDistance = distance;
    }

    this.controls.reset();
    this.controls.minPolarAngle = 0;
    this.controls.maxPolarAngle = Math.PI;

    this.controls.target.copy(this.defaultCameraTarget);
    this.camera.position.set(0, this.defaultCameraTarget.y, this.defaultCameraDistance);
    this.camera.lookAt(this.defaultCameraTarget);
    this.controls.update();
    this.setCameraPreset('isometric');

    // Zoom in 8 scroll steps (OrbitControls dolly factor: 0.95 per step)
    const zoomFactor = Math.pow(0.95, 8);
    const dir = this.camera.position.clone().sub(this.controls.target);
    this.camera.position.copy(this.controls.target.clone().add(dir.multiplyScalar(zoomFactor)));
    this.controls.update();

    // Rotate camera 30 degrees around Y axis for default orientation
    const rotDir = this.camera.position.clone().sub(this.controls.target);
    rotDir.applyAxisAngle(new THREE.Vector3(0, 1, 0), (-15 * Math.PI) / 180);
    this.camera.position.copy(this.controls.target.clone().add(rotDir));
    this.controls.update();

    const currentPolar = this.controls.getPolarAngle();
    this.controls.minPolarAngle = currentPolar;
    this.controls.maxPolarAngle = currentPolar;

    this.controls.saveState();

    // Update grid size after camera setup
    this.updateGridSize();
  }

  setCameraPreset(preset: CameraPreset) {
    const distance = this.camera.position.distanceTo(this.controls.target);
    const target = this.controls.target.clone();

    let position: THREE.Vector3;

    switch (preset) {
      case 'front':
        position = new THREE.Vector3(0, 0, distance);
        break;
      case 'back':
        position = new THREE.Vector3(0, 0, -distance);
        break;
      case 'left':
        position = new THREE.Vector3(-distance, 0, 0);
        break;
      case 'right':
        position = new THREE.Vector3(distance, 0, 0);
        break;
      case 'top':
        position = new THREE.Vector3(0, distance, 0);
        break;
      case 'bottom':
        position = new THREE.Vector3(0, -distance, 0);
        break;
      case 'isometric':
        // Front-left view matching ingame unit panel angle (~-20° elevation)
        position = new THREE.Vector3(-distance / 2.0, -distance / 16.0, distance / 2.0)
          .normalize()
          .multiplyScalar(distance);
        break;
      default:
        position = new THREE.Vector3(0, 0, distance);
    }

    position.add(target);
    this.camera.position.copy(position);
    this.controls.update();
  }

  takeScreenshot(): string | null {
    const glCanvas = this.renderer.domElement;
    const width = glCanvas.width;
    const height = glCanvas.height;

    if (!this.skyBackgroundImage) {
      return glCanvas.toDataURL('image/png');
    }

    const offscreen = document.createElement('canvas');
    offscreen.width = width;
    offscreen.height = height;
    const ctx = offscreen.getContext('2d');
    if (!ctx) return glCanvas.toDataURL('image/png');

    // Draw background with CSS cover semantics
    const img = this.skyBackgroundImage;
    const imgAspect = img.naturalWidth / img.naturalHeight;
    const canvasAspect = width / height;
    let drawW: number, drawH: number;
    if (imgAspect > canvasAspect) {
      drawH = height;
      drawW = height * imgAspect;
    } else {
      drawW = width;
      drawH = width / imgAspect;
    }
    const drawX = (width - drawW) / 2;
    const drawY = this.skyBackgroundPosition.includes('top') ? 0 : (height - drawH) / 2;
    ctx.drawImage(img, drawX, drawY, drawW, drawH);

    // Composite WebGL canvas on top
    ctx.drawImage(glCanvas, 0, 0);

    return offscreen.toDataURL('image/png');
  }

  getContent() {
    return this.content;
  }

  getPlatformScene() {
    return this.platformScene;
  }

  getCameraSyncState(sourceId?: string): CameraSyncState {
    return {
      position: this.camera.position.toArray() as [number, number, number],
      target: this.controls.target.toArray() as [number, number, number],
      zoom: this.camera.zoom,
      sourceId,
    };
  }

  applyCameraSyncState(state: CameraSyncState) {
    this.camera.position.fromArray(state.position);
    this.camera.zoom = state.zoom;
    this.camera.updateProjectionMatrix();
    this.controls.target.fromArray(state.target);
    this.controls.update();
  }

  onCameraChange(callback: () => void) {
    this.controls.addEventListener('change', callback);
    return () => {
      this.controls.removeEventListener('change', callback);
    };
  }

  // The platform / grid / lights / skeleton helpers live in groups other than this.content, so
  // they are naturally excluded from the export. Animations are not part of the scene graph and
  // must be passed in explicitly.
  async exportGlb(): Promise<ArrayBuffer | null> {
    if (!this.content) return null;
    const exporter = new GLTFExporter();
    return new Promise<ArrayBuffer | null>((resolve, reject) => {
      exporter.parse(
        this.content!,
        (result) => {
          if (result instanceof ArrayBuffer) resolve(result);
          else reject(new Error('GLTFExporter returned a non-binary result; expected ArrayBuffer'));
        },
        (error) => reject(error),
        { binary: true, animations: this.animationClips },
      );
    });
  }

  dispose() {
    this.disposed = true;

    // Stop animation loop
    if (this.animationFrameId !== null) {
      cancelAnimationFrame(this.animationFrameId);
    }

    // Remove resize observer
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;

    // Clear content
    this.clear();

    // Dispose lights
    this.lights.forEach((light) => {
      light.parent?.remove(light);
    });

    // Dispose helpers
    if (this.gridHelper) {
      this.scene.remove(this.gridHelper);
      this.gridHelper.geometry.dispose();
      (this.gridHelper.material as THREE.Material).dispose();
    }
    if (this.axesHelper) {
      this.scene.remove(this.axesHelper);
      // Dispose Line2 children (thick axes)
      this.axesHelper.traverse((child) => {
        if (child instanceof Line2) {
          child.geometry.dispose();
          (child.material as LineMaterial).dispose();
        }
      });
    }
    if (this.skeletonHelper) {
      this.scene.remove(this.skeletonHelper);
      this.skeletonHelper.geometry.dispose();
      (this.skeletonHelper.material as THREE.Material).dispose();
    }

    // Dispose stats
    if (this.stats && this.stats.dom.parentElement) {
      this.stats.dom.parentElement.removeChild(this.stats.dom);
    }

    // Dispose environment textures
    this.neutralEnvironment?.dispose();
    this.gameEnvironment?.dispose();
    this.gameBackground?.dispose();

    // Dispose PMREM
    this.pmremGenerator.dispose();

    // Dispose controls
    this.controls.dispose();

    // Dispose renderer
    this.renderer.dispose();

    // Remove canvas
    if (this.renderer.domElement.parentElement) {
      this.renderer.domElement.parentElement.removeChild(this.renderer.domElement);
    }
  }
}

const ModelViewer = forwardRef<ModelViewerHandle, ModelViewerProps>(
  function ModelViewer(
    {
      glbUrl,
      onModelLoaded,
      statsContainer,
      unitScale,
      faction,
      customTextureUrl,
      customTextureUrls,
      onTextureSlotsReady,
      syncStore = true,
      cameraSyncId = 'viewer',
      cameraSyncState,
      onCameraSyncStateChange,
    },
    ref,
  ) {
    const containerRef = useRef<HTMLDivElement>(null);
    const viewerRef = useRef<ThreeViewer | null>(null);
    // Set while we are programmatically applying an inbound camera state, so the resulting
    // controls.change event does not bounce right back out and create a sync loop.
    const applyingCameraSyncRef = useRef(false);
    // Held in a ref so the load-model effect can fire a one-shot seed without listing this prop
    // in its dependency array; otherwise ViewerPage's `compareMode ? handler : undefined` toggle
    // would identity-flip on every Compare on/off and force a full GLB re-fetch.
    const onCameraSyncStateChangeRef = useRef(onCameraSyncStateChange);
    onCameraSyncStateChangeRef.current = onCameraSyncStateChange;
    const [viewerReady, setViewerReady] = useState(false);
    const [loading, setLoading] = useState(true);
    const [layoutReady, setLayoutReady] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const modelUrlRef = useRef<string | null>(null);
    const { label } = useLabels();

    // Store selectors
    const {
      displayMode,
      showPlatform,
      showBackground,
      backgroundColor,
      showGrid,
      usePunctualLights,
      ambientIntensity,
      ambientColor,
      directionalIntensity,
      directionalColor,
      toneMapping,
      exposure,
      pixelRatioLimit,
      autoRotate,
      autoRotateSpeed,
      nearClip,
      farClip,
      showStats,
      showSkeleton,
      pointSize,
      materials,
      globalWireframe,
      hiddenNodes,
      soloNode,
      boundingBoxNodes,
      wireframeNodes,
      animations: storeAnimations,
      playbackSpeed,
      loopMode,
      setAnimations,
      setMaterials,
      setSceneGraph,
      setPlatformSceneGraph,
      setShowGrid,
      setGlobalWireframe,
    } = useViewerStore();

    // Expose imperative methods
    useImperativeHandle(ref, () => ({
      takeScreenshot: () => viewerRef.current?.takeScreenshot() ?? null,
      resetCamera: () => viewerRef.current?.resetCamera(),
      setCameraPreset: (preset: CameraPreset) => viewerRef.current?.setCameraPreset(preset),
      getCameraState: () => viewerRef.current?.getCameraSyncState(cameraSyncId) ?? null,
      applyCameraState: (state: CameraSyncState) => viewerRef.current?.applyCameraSyncState(state),
      exportGlb: () => viewerRef.current?.exportGlb() ?? Promise.resolve(null),
    }), [cameraSyncId]);

    // Initialize viewer
    useEffect(() => {
      if (!containerRef.current) return;

      const viewer = new ThreeViewer(containerRef.current, statsContainer);
      viewerRef.current = viewer;
      setViewerReady(true);

      return () => {
        viewer.dispose();
        viewerRef.current = null;
        setViewerReady(false);
      };
    }, [statsContainer]);

    // Emit camera state changes (debounced to one per frame) so a peer viewer can mirror them.
    useEffect(() => {
      if (!viewerReady || !onCameraSyncStateChange) return;
      const viewer = viewerRef.current;
      if (!viewer) return;

      let rafId: number | null = null;

      const emitCameraState = () => {
        if (applyingCameraSyncRef.current) return;
        if (rafId !== null) cancelAnimationFrame(rafId);
        rafId = requestAnimationFrame(() => {
          rafId = null;
          onCameraSyncStateChange(viewer.getCameraSyncState(cameraSyncId));
        });
      };

      const cleanup = viewer.onCameraChange(emitCameraState);
      emitCameraState();

      return () => {
        if (rafId !== null) cancelAnimationFrame(rafId);
        cleanup();
      };
    }, [viewerReady, cameraSyncId, onCameraSyncStateChange]);

    // Apply inbound camera state from a peer viewer. Ignore echoes that originated from this viewer.
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || !cameraSyncState || cameraSyncState.sourceId === cameraSyncId) return;

      applyingCameraSyncRef.current = true;
      viewer.applyCameraSyncState(cameraSyncState);

      const rafId = requestAnimationFrame(() => {
        applyingCameraSyncRef.current = false;
      });

      return () => {
        cancelAnimationFrame(rafId);
        applyingCameraSyncRef.current = false;
      };
    }, [cameraSyncState, cameraSyncId]);

    // Load model when URL changes
    useEffect(() => {
      if (!viewerReady) return;
      const viewer = viewerRef.current;
      if (!viewer) return;

      let blobUrl: string | null = null;
      let canceled = false;

      const loadModel = async () => {
        try {
          setLoading(true);
          setError(null);

          // Fetch model as blob
          const response = await fetch(glbUrl);
          if (!response.ok) {
            throw new Error(`Failed to load model: ${response.statusText}`);
          }

          const blob = await response.blob();
          blobUrl = URL.createObjectURL(blob);

          if (canceled) {
            URL.revokeObjectURL(blobUrl);
            return;
          }

          modelUrlRef.current = blobUrl;

          // Load the model
          const { scene, animations } = await viewer.loadModel(blobUrl);

          if (canceled) return;

          // Set content and get materials/scene graph
          // Skip camera setup in game-preview mode (camera is based on platform, not model)
          const skipCameraSetup = displayMode === 'game-preview';
          const { materials: extractedMaterials, sceneGraph, textureSlots } = viewer.setContent(
            scene,
            animations,
            syncStore
              ? (animStates) => {
                  setAnimations(animStates);
                }
              : undefined,
            skipCameraSetup
          );

          // Compare-mode secondary viewer must not write to the shared store, otherwise the two
          // viewers fight each other over materials/scene/animations.
          if (syncStore) {
            setMaterials(extractedMaterials);
            setSceneGraph(sceneGraph);
            onTextureSlotsReady?.(textureSlots);
            onModelLoaded?.();
          }

          setLoading(false);
          setLayoutReady(displayMode !== 'game-preview');

          // Seed the peer with our current camera state once the model is in place. Read through
          // a ref so the load effect does not list this callback in its deps.
          onCameraSyncStateChangeRef.current?.(viewer.getCameraSyncState(cameraSyncId));
        } catch (err) {
          if (!canceled) {
            setError(err instanceof Error ? err.message : 'Unknown error');
            setLoading(false);
          }
        }
      };

      loadModel();

      return () => {
        canceled = true;
        if (blobUrl) {
          URL.revokeObjectURL(blobUrl);
        }
      };
      // onCameraSyncStateChange intentionally omitted - it is read via a ref so the load effect
      // does not re-fetch the GLB every time ViewerPage toggles compareMode.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [
      viewerReady,
      glbUrl,
      displayMode,
      onModelLoaded,
      onTextureSlotsReady,
      setAnimations,
      setMaterials,
      setSceneGraph,
      syncStore,
      cameraSyncId,
    ]);

    // Apply custom textures whenever the URL map changes (after the model has finished loading).
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || loading) return;

      let canceled = false;
      const nextUrls =
        customTextureUrls ?? (customTextureUrl ? { 'baseColor:0': customTextureUrl } : {});

      viewer.setCustomTextureUrls(nextUrls).catch((err) => {
        if (!canceled) console.warn('Failed to apply custom texture:', err);
      });

      return () => {
        canceled = true;
      };
    }, [customTextureUrl, customTextureUrls, loading]);

    // Reset layoutReady when displayMode changes
    useEffect(() => {
      if (displayMode === 'game-preview') {
        setLayoutReady(false);
      } else {
        setLayoutReady(true);
      }
    }, [displayMode]);

    // Sync animations with store
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || loading) return;
      viewer.syncAnimations(storeAnimations, loopMode);
      // Also sync platform animations (loop mode only, they're always playing)
      viewer.syncPlatformAnimations(loopMode);
    }, [storeAnimations, loopMode, loading]);

    // Playback speed
    useEffect(() => {
      viewerRef.current?.setPlaybackSpeed(playbackSpeed);
    }, [playbackSpeed]);

    // Background and environment
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer) return;
      viewer.setBackground(showBackground, backgroundColor, displayMode);
      viewer.setEnvironment(displayMode);
    }, [showBackground, backgroundColor, displayMode]);

    // Orbit mode: turntable for Game Preview, free for Studio
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || !viewerReady) return;
      viewer.setOrbitMode(displayMode === 'game-preview' ? 'turntable' : 'free');
    }, [viewerReady, displayMode]);

    // Tone mapping
    useEffect(() => {
      viewerRef.current?.setToneMapping(toneMapping, exposure);
    }, [toneMapping, exposure]);

    // Pixel ratio
    useEffect(() => {
      viewerRef.current?.setPixelRatioLimit(pixelRatioLimit);
    }, [pixelRatioLimit]);

    // Lights
    useEffect(() => {
      if (!viewerReady) return;
      viewerRef.current?.updateLights(
        usePunctualLights,
        ambientIntensity,
        ambientColor,
        directionalIntensity,
        directionalColor
      );
    }, [viewerReady, usePunctualLights, ambientIntensity, ambientColor, directionalIntensity, directionalColor]);

    // Grid + Axes (combined like donmccurdy viewer)
    useEffect(() => {
      viewerRef.current?.setGrid(showGrid);
    }, [showGrid]);

    // Skeleton
    useEffect(() => {
      viewerRef.current?.setSkeleton(showSkeleton);
    }, [showSkeleton]);

    // Stats
    useEffect(() => {
      viewerRef.current?.setStats(showStats);
    }, [showStats]);

    // Auto rotate
    useEffect(() => {
      viewerRef.current?.setAutoRotate(autoRotate, autoRotateSpeed);
    }, [autoRotate, autoRotateSpeed]);

    // Clip planes
    useEffect(() => {
      viewerRef.current?.setClipPlanes(nearClip, farClip);
    }, [nearClip, farClip]);

    // Materials
    useEffect(() => {
      viewerRef.current?.updateMaterials(materials, globalWireframe, pointSize);
    }, [materials, globalWireframe, pointSize]);

    // Visibility
    useEffect(() => {
      viewerRef.current?.updateVisibility(hiddenNodes, soloNode);
    }, [hiddenNodes, soloNode]);

    // Phase 2: Bounding box helpers
    useEffect(() => {
      viewerRef.current?.syncBoundingBoxHelpers(boundingBoxNodes);
    }, [boundingBoxNodes]);

    // Phase 2: Bone wireframe helpers
    useEffect(() => {
      viewerRef.current?.syncWireframeHelpers(wireframeNodes);
    }, [wireframeNodes]);

    // Platform visibility
    useEffect(() => {
      viewerRef.current?.setPlatformVisible(displayMode === 'game-preview' && showPlatform);
    }, [displayMode, showPlatform]);

    // Load platform for game-preview mode
    // Depends on viewerReady to ensure we reload platform when viewer is recreated (e.g., model switch with key change)
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || !viewerReady || displayMode !== 'game-preview') {
        // Clear platform scene graph when not in game-preview mode or viewer not ready.
        // Only the store-owning viewer touches the shared store.
        if (displayMode !== 'game-preview' && syncStore) {
          setPlatformSceneGraph(null);
        }
        return;
      }

      let canceled = false;
      let platformBlobUrl: string | null = null;

      const loadPlatform = async () => {
        try {
          const response = await fetch('/api/viewer/platform');
          if (!response.ok || canceled) return;

          const blob = await response.blob();
          platformBlobUrl = URL.createObjectURL(blob);

          if (canceled) {
            URL.revokeObjectURL(platformBlobUrl);
            return;
          }

          const { sceneGraph } = await viewer.loadPlatform(platformBlobUrl);
          if (!canceled) {
            if (syncStore) setPlatformSceneGraph(sceneGraph);
            // Apply current animation settings to platform
            viewer.syncPlatformAnimations(loopMode);
            viewer.setPlaybackSpeed(playbackSpeed);
          }
        } catch (err) {
          console.warn('Failed to load platform:', err);
        }
      };

      loadPlatform();

      return () => {
        canceled = true;
        if (syncStore) setPlatformSceneGraph(null);
        if (platformBlobUrl) {
          URL.revokeObjectURL(platformBlobUrl);
        }
      };
      // loopMode and playbackSpeed are intentionally excluded - they're synced by separate effects
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [viewerReady, displayMode, setPlatformSceneGraph, syncStore]);

    // Position platform under model (only after BOTH model AND platform are fully loaded)
    useEffect(() => {
      const viewer = viewerRef.current;
      // Only run when: viewer ready, game-preview mode, platform visible, model loaded
      if (!viewer || displayMode !== 'game-preview' || !showPlatform || loading) return;

      let rafId: number;
      let waitingFrames = 0;
      let platformReadyFrames = 0;

      const tryLayout = () => {
        // Check if platform is ready (viewer internal state)
        const platformReady = viewer.getPlatformScene();
        if (!platformReady) {
          // Platform not loaded yet - keep polling indefinitely
          waitingFrames++;
          if (waitingFrames === 1) {
            console.log('[Layout] Waiting for platform to load...');
          }
          rafId = requestAnimationFrame(tryLayout);
          return;
        }

        // Platform is ready, wait a few more frames for bounding boxes to be calculated
        platformReadyFrames++;
        if (platformReadyFrames < 3) {
          rafId = requestAnimationFrame(tryLayout);
          return;
        }

        // Everything ready, run layout
        viewer.layoutGamePreview(unitScale ?? null);
        setLayoutReady(true);
      };

      rafId = requestAnimationFrame(tryLayout);

      return () => {
        cancelAnimationFrame(rafId);
      };
    }, [displayMode, showPlatform, loading, unitScale]);

    // Load game-preview environment (Cold Sunset equirect for IBL)
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer || displayMode !== 'game-preview') return;

      let canceled = false;
      let environmentBlobUrl: string | null = null;

      const loadGameAssets = async () => {
        try {
          const envResponse = await fetch('/api/viewer/environment');
          if (canceled) return;

          if (envResponse.ok) {
            const blob = await envResponse.blob();
            environmentBlobUrl = URL.createObjectURL(blob);
            if (!canceled) {
              await viewer.loadGameEnvironment(environmentBlobUrl);
              viewer.setEnvironment(displayMode);
            }
          }
        } catch (err) {
          console.warn('Failed to load game-preview environment:', err);
        }
      };

      loadGameAssets();

      return () => {
        canceled = true;
        if (environmentBlobUrl) URL.revokeObjectURL(environmentBlobUrl);
      };
    }, [displayMode]);

    // Load faction-specific background as CSS background (unit_background primary, sky fallback)
    useEffect(() => {
      const viewer = viewerRef.current;
      if (!viewer) return;

      if (displayMode !== 'game-preview' || !faction) {
        viewer.setCanvasSkyBackground(null);
        return;
      }

      let canceled = false;
      let bgBlobUrl: string | null = null;

      const loadBackground = async () => {
        try {
          let response = await fetch(`/api/viewer/unit-background/${encodeURIComponent(faction)}`);
          if (canceled) return;

          let position = 'center center';
          if (!response.ok) {
            response = await fetch(`/api/viewer/sky/${encodeURIComponent(faction)}`);
            if (canceled || !response.ok) return;
            position = 'center top';
          }

          const blob = await response.blob();
          bgBlobUrl = URL.createObjectURL(blob);
          if (!canceled) viewer.setCanvasSkyBackground(bgBlobUrl, position);
        } catch (err) {
          console.warn('Failed to load faction background:', err);
        }
      };

      loadBackground();

      return () => {
        canceled = true;
        if (bgBlobUrl) {
          URL.revokeObjectURL(bgBlobUrl);
          viewer.setCanvasSkyBackground(null);
        }
      };
    }, [displayMode, faction]);

    // Keyboard shortcuts
    useEffect(() => {
      const handleKeyDown = (e: KeyboardEvent) => {
        if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) {
          return;
        }

        switch (e.key.toLowerCase()) {
          case 'r':
            viewerRef.current?.resetCamera();
            break;
          case 'g':
            setShowGrid(!showGrid);
            break;
          case 'w':
            setGlobalWireframe(!globalWireframe);
            break;
        }
      };

      window.addEventListener('keydown', handleKeyDown);
      return () => window.removeEventListener('keydown', handleKeyDown);
    }, [showGrid, globalWireframe, setShowGrid, setGlobalWireframe]);

    return (
      <div
        ref={containerRef}
        className="w-full h-full bg-[#191919]"
        style={{ position: 'relative' }}
      >
        {/* Loading overlay */}
        {(loading || (displayMode === 'game-preview' && !layoutReady)) && (
          <div className="absolute inset-0 flex items-center justify-center bg-[#191919] text-gray-500 z-10">
            <div className="text-center">
              <div className="w-10 h-10 border-[3px] border-[#333] border-t-[#646cff] rounded-full animate-spin mx-auto mb-4" />
              <p>{label('viewer_model_loading')}</p>
            </div>
          </div>
        )}

        {/* Error overlay */}
        {error && (
          <div className="absolute inset-0 flex flex-col items-center justify-center bg-[#191919] gap-3 z-10">
            <p className="text-red-500 text-base">{label('viewer_model_failed')}</p>
            <p className="text-gray-600 text-sm">{error}</p>
          </div>
        )}
      </div>
    );
  }
);

export default ModelViewer;
