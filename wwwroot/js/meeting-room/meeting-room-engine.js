(function () {
    const defaultMap = {
        tileSize: 32,
        floor: {
            baseColor: 0xefe6d8,
            groutColor: 0xe8b889,
            markColor: 0xeec49b
        }
    };

    function normalizeMap(map) {
        return {
            ...defaultMap,
            ...(map || {}),
            floor: {
                ...defaultMap.floor,
                ...((map || {}).floor || {})
            }
        };
    }

    async function loadMap(url) {
        if (!url) return defaultMap;

        try {
            const response = await fetch(url, { cache: 'no-store' });
            if (!response.ok) return defaultMap;
            return normalizeMap(await response.json());
        } catch {
            return defaultMap;
        }
    }

    function drawFloor(scene, mapData) {
        const width = scene.scale.width;
        const height = scene.scale.height;
        const tileSize = Number(mapData.tileSize || 32);
        const graphics = scene.add.graphics();

        graphics.fillStyle(mapData.floor.baseColor, 1);
        graphics.fillRect(0, 0, width, height);

        graphics.lineStyle(1, mapData.floor.groutColor, .45);
        for (let y = 0; y <= height + tileSize; y += tileSize) {
            graphics.lineBetween(0, y, width, y);
            const offset = Math.floor(y / tileSize) % 2 === 0 ? 0 : tileSize * 1.5;
            for (let x = -offset; x <= width + tileSize * 3; x += tileSize * 3) {
                graphics.lineBetween(x, y, x, y + tileSize * .38);
            }
        }

        graphics.lineStyle(1, mapData.floor.markColor, .22);
        for (let x = 0; x <= width; x += tileSize * 2.35) {
            graphics.lineBetween(x, 0, x, height);
        }
    }

    async function mount(options) {
        const host = options?.host;
        if (!host || !window.Phaser) {
            return null;
        }

        const mapData = await loadMap(options.mapUrl);
        host.replaceChildren();

        class MeetingRoomScene extends Phaser.Scene {
            constructor() {
                super('MeetingRoomScene');
            }

            create() {
                drawFloor(this, mapData);
                this.scale.on('resize', () => {
                    this.scene.restart();
                });
            }
        }

        const game = new Phaser.Game({
            type: Phaser.AUTO,
            parent: host,
            transparent: true,
            width: host.clientWidth || 1280,
            height: host.clientHeight || 720,
            scale: {
                mode: Phaser.Scale.RESIZE,
                autoCenter: Phaser.Scale.NO_CENTER
            },
            render: {
                pixelArt: true,
                antialias: false
            },
            scene: MeetingRoomScene
        });

        return {
            game,
            destroy() {
                game.destroy(true);
            }
        };
    }

    window.MeetingRoomEngine = {
        mount
    };
})();
