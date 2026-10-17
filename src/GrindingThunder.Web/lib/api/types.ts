export type Guid = string;

export const ResearchTreeVersionStatus = {
  Draft: 0,
  Published: 1,
} as const;

export type ResearchTreeVersionStatus =
  (typeof ResearchTreeVersionStatus)[keyof typeof ResearchTreeVersionStatus];

export const VehicleAcquisitionType = {
  Researchable: 0,
  Premium: 1,
  Squadron: 2,
  Event: 3,
  Pack: 4,
  Hidden: 5,
} as const;

export type VehicleAcquisitionType =
  (typeof VehicleAcquisitionType)[keyof typeof VehicleAcquisitionType];

export type ResearchOptimizationMode =
  | "LeastRp"
  | "LeastSl"
  | "FewestVehicles"
  | "FewestMatches";

export interface TreeRankSummary {
  id: Guid;
  rankNumber: number;
  requiredVehiclesUnlocked: number;
}

export interface ResearchTreeVersionSummary {
  id: Guid;
  gameUpdateId: Guid;
  status: ResearchTreeVersionStatus;
  updateVersion: string;
  treeRanks: TreeRankSummary[];
}

export interface ResearchTreeSummary {
  id: Guid;
  vehicleTypeId: Guid;
  vehicleTypeName: string;
  versions: ResearchTreeVersionSummary[];
}

export interface NationSummary {
  id: Guid;
  name: string;
  researchTrees: ResearchTreeSummary[];
}

export interface VehicleTreeEntry {
  id: Guid;
  researchTreeId: Guid;
  nationId: Guid;
  nationName: string;
  vehicleTypeId: Guid;
  vehicleTypeName: string;
  treeRankId: Guid;
  researchTreeVersionId: Guid;
  name: string;
  imageUrl: string | null;
  wikiUrl: string | null;
  rpCost: number;
  slCost: number;
  acquisitionType: VehicleAcquisitionType;
  isFolderParent: boolean;
  folderParentId: Guid | null;
  treeColumn: number;
  treeRow: number;
  prerequisiteIds: Guid[];
}

export interface PlayerRegisterRequest {
  username: string;
  email: string;
  password: string;
}

export interface PlayerLoginRequest {
  usernameOrEmail: string;
  password: string;
}

export interface PlayerTokenResponse {
  playerAccountId: Guid;
  username: string;
  email: string;
  token: string;
  tokenType: string;
  expiresAt: string;
}

export interface PlayerProfile {
  playerAccountId: Guid;
  username: string;
  email: string;
}

export interface PlayerCalculatorPreferences {
  optimizationMode: ResearchOptimizationMode;
  averageRpPerMatch: number | null;
  averageNetSlPerMatch: number | null;
}

export interface PlayerProgress {
  unlockedVehicleIds: Guid[];
  preferences: PlayerCalculatorPreferences;
}

export interface UpdatePlayerProgressRequest {
  unlockedVehicleIds: Guid[];
  preferences: PlayerCalculatorPreferences;
}

export interface ResearchCalculationRequest {
  researchTreeVersionId: Guid;
  targetVehicleId: Guid;
  unlockedVehicleIds: Guid[];
  fillerTargetIds: Guid[];
  averageRpPerMatch: number;
  averageNetSlPerMatch: number;
  optimizationMode: ResearchOptimizationMode;
}

export interface VehicleResearchSummary {
  vehicleId: Guid;
  name: string;
  rpRemaining: number;
  slRemaining: number;
  isRankGateFiller: boolean;
}

export interface RankDeficit {
  rankNumber: number;
  shortfall: number;
  required: number;
}

export interface ResearchCalculationResult {
  targetVehicleId: Guid;
  totalRpRequired: number;
  totalSlRequired: number;
  rpEstimatedMatches: number;
  slEstimatedMatches: number;
  estimatedMatches: number;
  requiredVehicles: VehicleResearchSummary[];
  rankDeficits: RankDeficit[];
}

export interface ApiProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
  message?: string;
}
