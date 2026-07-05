type MeetingRoomFloor = {
    baseColor: number;
    groutColor: number;
    markColor: number;
};

type MeetingRoomTileMap = {
    tileSize: number;
    floor: MeetingRoomFloor;
};

type MeetingRoomEngineOptions = {
    host: HTMLElement | null;
    mapUrl?: string;
};

type MeetingRoomEngineHandle = {
    game: unknown;
    destroy(): void;
};

declare global {
    interface Window {
        Phaser?: unknown;
        MeetingRoomEngine?: {
            mount(options: MeetingRoomEngineOptions): Promise<MeetingRoomEngineHandle | null>;
        };
    }
}

export {};
