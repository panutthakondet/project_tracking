(function () {
    'use strict';

    const config = document.getElementById('globalMeetingAudio');
    if (!config || config.dataset.enabled !== 'true') return;

    const currentUserId = Number(config.dataset.currentUserId || 0);
    if (!currentUserId) return;

    const instanceId = `layout-${Date.now()}-${Math.random().toString(36).slice(2)}`;
    const stateUrl = config.dataset.stateUrl || '';
    const liveKitTokenUrl = config.dataset.livekitTokenUrl || '';
    const hubUrl = config.dataset.hubUrl || '';
    const antiForgeryToken = config.dataset.antiForgeryToken || '';

    const desiredMicKey = 'meetingRoom:desiredMicOn';
    const audioOwnerKey = 'meetingRoom:audioOwner';
    const globalLeaderKey = 'meetingRoom:globalAudioLeader';
    const leaseMs = 7000;
    const pollMs = 12000;

    const runtime = {
        room: null,
        connectedAreaKey: '',
        connecting: null,
        hub: null,
        isLeader: false,
        pollTimer: 0,
        leaseTimer: 0,
        ownerTimer: 0,
        remoteAudio: new Map(),
        lastState: null,
        speakingTimer: 0,
        speakingContext: null,
        isSpeaking: false,
        unlockButton: null
    };

    function now() {
        return Date.now();
    }

    function readJsonStorage(key) {
        try {
            const raw = window.localStorage.getItem(key);
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    function writeJsonStorage(key, value) {
        try {
            window.localStorage.setItem(key, JSON.stringify(value));
        } catch {
            // Storage can be disabled in private mode. The listener still works in one tab.
        }
    }

    function removeStorage(key) {
        try {
            window.localStorage.removeItem(key);
        } catch {
            // Ignore storage cleanup failures.
        }
    }

    function readDesiredMicOn() {
        const saved = readJsonStorage(desiredMicKey);
        if (!saved || saved.value !== true) return false;
        const updatedAt = Number(saved.updatedAt || 0);
        return updatedAt > 0 && now() - updatedAt < 12 * 60 * 60 * 1000;
    }

    function normalizeAreaKey(value) {
        return String(value || '')
            .trim()
            .toLowerCase()
            .replace(/[^a-z0-9ก-๙]+/g, '-')
            .replace(/^-+|-+$/g, '') || 'open-area';
    }

    function liveKitClient() {
        return window.LivekitClient || window.LiveKitClient || null;
    }

    function activeMeetingRoomOwner() {
        const owner = readJsonStorage(audioOwnerKey);
        return owner &&
            owner.scope === 'meeting-room' &&
            owner.id !== instanceId &&
            Number(owner.expiresAt || 0) > now();
    }

    function renewLayoutOwner() {
        writeJsonStorage(audioOwnerKey, {
            id: instanceId,
            scope: 'layout',
            userId: currentUserId,
            expiresAt: now() + leaseMs
        });
    }

    function releaseLayoutOwner() {
        const owner = readJsonStorage(audioOwnerKey);
        if (owner?.id === instanceId) {
            removeStorage(audioOwnerKey);
        }
    }

    function acquireLeadership() {
        if (activeMeetingRoomOwner()) {
            runtime.isLeader = false;
            return false;
        }

        const leader = readJsonStorage(globalLeaderKey);
        if (leader && leader.id !== instanceId && Number(leader.expiresAt || 0) > now()) {
            runtime.isLeader = false;
            return false;
        }

        writeJsonStorage(globalLeaderKey, {
            id: instanceId,
            userId: currentUserId,
            expiresAt: now() + leaseMs
        });
        runtime.isLeader = true;
        renewLayoutOwner();
        return true;
    }

    function releaseLeadership() {
        const leader = readJsonStorage(globalLeaderKey);
        if (leader?.id === instanceId) {
            removeStorage(globalLeaderKey);
        }
        runtime.isLeader = false;
        releaseLayoutOwner();
    }

    function ensureUnlockButton() {
        if (runtime.unlockButton) return;

        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = 'เปิดเสียง Meeting';
        button.style.cssText = [
            'position:fixed',
            'right:18px',
            'bottom:18px',
            'z-index:2147483000',
            'border:0',
            'border-radius:999px',
            'padding:10px 14px',
            'font:600 14px Prompt,system-ui,sans-serif',
            'color:#fff',
            'background:#111827',
            'box-shadow:0 10px 24px rgba(15,23,42,.28)',
            'cursor:pointer'
        ].join(';');
        button.addEventListener('click', () => {
            let unlocked = true;
            runtime.remoteAudio.forEach(audio => {
                const playResult = audio.play?.();
                if (playResult?.catch) {
                    playResult.catch(() => {
                        unlocked = false;
                    });
                }
            });

            if (unlocked) {
                button.remove();
                runtime.unlockButton = null;
            }
        });

        document.body.appendChild(button);
        runtime.unlockButton = button;
    }

    function playAudioElement(audio) {
        const result = audio.play?.();
        if (result?.catch) {
            result.catch(() => ensureUnlockButton());
        }
    }

    function trackKey(track, publication, participant) {
        return [
            participant?.identity || participant?.sid || 'participant',
            publication?.trackSid || publication?.sid || track?.sid || track?.mediaStreamTrack?.id || 'track'
        ].join(':');
    }

    function trackKind(track, publication) {
        return String(track?.kind || publication?.kind || '').toLowerCase();
    }

    function isAudioTrack(track, publication) {
        return trackKind(track, publication) === 'audio';
    }

    function attachRemoteAudio(track, publication, participant) {
        if (!isAudioTrack(track, publication)) return;
        if (participant?.identity === `user-${currentUserId}`) return;

        const key = trackKey(track, publication, participant);
        let audio = runtime.remoteAudio.get(key);
        if (!audio) {
            audio = document.createElement('audio');
            audio.autoplay = true;
            audio.playsInline = true;
            audio.hidden = true;
            audio.dataset.meetingRoomGlobalAudio = 'true';
            document.body.appendChild(audio);
            runtime.remoteAudio.set(key, audio);
        }

        try {
            if (typeof track.attach === 'function') {
                track.attach(audio);
            } else if (track.mediaStreamTrack) {
                audio.srcObject = new MediaStream([track.mediaStreamTrack]);
            }
            playAudioElement(audio);
        } catch {
            // A failed remote attachment should not break the rest of the page.
        }
    }

    function detachRemoteAudio(track, publication, participant) {
        const key = trackKey(track, publication, participant);
        const audio = runtime.remoteAudio.get(key);
        if (!audio) return;

        try {
            if (typeof track?.detach === 'function') {
                track.detach(audio);
            }
        } catch {
            // Best effort cleanup.
        }

        audio.srcObject = null;
        audio.remove();
        runtime.remoteAudio.delete(key);
    }

    function rememberExistingRemoteTracks(room) {
        room.remoteParticipants?.forEach?.(participant => {
            participant.trackPublications?.forEach?.(publication => {
                const track = publication.track;
                if (track && !publication.isMuted) {
                    attachRemoteAudio(track, publication, participant);
                }
            });
        });
    }

    async function postLiveKitToken(areaKey, areaTitle) {
        const response = await fetch(liveKitTokenUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                'RequestVerificationToken': antiForgeryToken
            },
            body: new URLSearchParams({ areaKey, areaTitle })
        });

        const payload = await response.json().catch(() => ({ ok: false }));
        if (!response.ok || payload.ok === false) {
            throw new Error(payload.message || 'LiveKit token failed.');
        }

        return payload;
    }

    function startSpeakingDetector(stream) {
        stopSpeakingDetector();
        if (!stream) return;

        try {
            const AudioContextType = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextType) return;

            const context = new AudioContextType();
            const source = context.createMediaStreamSource(stream);
            const analyser = context.createAnalyser();
            analyser.fftSize = 512;
            source.connect(analyser);

            const values = new Uint8Array(analyser.frequencyBinCount);
            runtime.speakingContext = context;
            runtime.speakingTimer = window.setInterval(() => {
                analyser.getByteTimeDomainData(values);
                let total = 0;
                for (const value of values) {
                    const centered = value - 128;
                    total += centered * centered;
                }
                const volume = Math.sqrt(total / values.length);
                const nextSpeaking = volume > 8;
                if (nextSpeaking !== runtime.isSpeaking) {
                    runtime.isSpeaking = nextSpeaking;
                    void publishMediaState({ isSpeaking: nextSpeaking });
                }
            }, 220);
        } catch {
            // Speaking dots are advisory; audio remains connected.
        }
    }

    function stopSpeakingDetector() {
        window.clearInterval(runtime.speakingTimer);
        runtime.speakingTimer = 0;
        runtime.isSpeaking = false;

        if (runtime.speakingContext) {
            runtime.speakingContext.close?.().catch?.(() => {});
            runtime.speakingContext = null;
        }
    }

    function localAudioStream(room) {
        const publications = room?.localParticipant?.trackPublications;
        if (!publications?.forEach) return null;

        let mediaTrack = null;
        publications.forEach(publication => {
            const track = publication.track;
            if (!mediaTrack && isAudioTrack(track, publication) && track?.mediaStreamTrack?.readyState === 'live') {
                mediaTrack = track.mediaStreamTrack;
            }
        });

        return mediaTrack ? new MediaStream([mediaTrack]) : null;
    }

    async function syncDesiredMicrophone() {
        const room = runtime.room;
        if (!room?.localParticipant) return;

        const shouldEnable = readDesiredMicOn();
        try {
            await room.localParticipant.setMicrophoneEnabled(shouldEnable, {
                echoCancellation: true,
                noiseSuppression: true,
                autoGainControl: true
            });

            if (shouldEnable) {
                startSpeakingDetector(localAudioStream(room));
            } else {
                stopSpeakingDetector();
            }

            await publishMediaState({ micOn: shouldEnable, isSpeaking: false });
        } catch {
            await publishMediaState({ micOn: false, isSpeaking: false });
        }
    }

    async function publishMediaState(extra = {}) {
        if (!runtime.hub || String(runtime.hub.state) !== 'Connected') return;
        const areaKey = normalizeAreaKey(runtime.lastState?.areaKey || runtime.connectedAreaKey || '');
        if (!areaKey) return;

        const micOn = Object.prototype.hasOwnProperty.call(extra, 'micOn') ? Boolean(extra.micOn) : readDesiredMicOn();
        const isSpeaking = micOn && Boolean(extra.isSpeaking);
        try {
            await runtime.hub.invoke('UpdateMediaState', areaKey, micOn, false, false, isSpeaking);
        } catch {
            // Realtime media state is best effort.
        }
    }

    function wireLiveKitRoom(room) {
        const events = liveKitClient()?.RoomEvent || {};

        room.on(events.TrackSubscribed || 'trackSubscribed', (track, publication, participant) => {
            attachRemoteAudio(track, publication, participant);
        });
        room.on(events.TrackUnsubscribed || 'trackUnsubscribed', (track, publication, participant) => {
            detachRemoteAudio(track, publication, participant);
        });
        room.on(events.TrackMuted || 'trackMuted', (publication, participant) => {
            detachRemoteAudio(publication?.track, publication, participant);
        });
        room.on(events.TrackUnmuted || 'trackUnmuted', (publication, participant) => {
            if (publication?.track) {
                attachRemoteAudio(publication.track, publication, participant);
            }
        });
        room.on(events.ParticipantDisconnected || 'participantDisconnected', participant => {
            [...runtime.remoteAudio.keys()]
                .filter(key => key.startsWith(`${participant?.identity || participant?.sid}:`))
                .forEach(key => {
                    runtime.remoteAudio.get(key)?.remove();
                    runtime.remoteAudio.delete(key);
                });
        });
        room.on(events.LocalTrackPublished || 'localTrackPublished', () => {
            startSpeakingDetector(localAudioStream(room));
            void publishMediaState({ micOn: readDesiredMicOn(), isSpeaking: false });
        });
        room.on(events.LocalTrackUnpublished || 'localTrackUnpublished', () => {
            stopSpeakingDetector();
            void publishMediaState({ micOn: false, isSpeaking: false });
        });
    }

    async function disconnectRoom() {
        const room = runtime.room;
        runtime.room = null;
        runtime.connectedAreaKey = '';
        runtime.connecting = null;
        stopSpeakingDetector();

        runtime.remoteAudio.forEach(audio => audio.remove());
        runtime.remoteAudio.clear();

        if (room) {
            try {
                room.disconnect();
            } catch {
                // Best effort disconnect.
            }
        }
    }

    async function connectRoom(areaKey, areaTitle) {
        if (!runtime.isLeader) return false;

        areaKey = normalizeAreaKey(areaKey);
        if (!areaKey || !liveKitTokenUrl) return false;
        if (runtime.room && runtime.connectedAreaKey === areaKey) {
            await syncDesiredMicrophone();
            return true;
        }

        if (runtime.connecting) {
            await runtime.connecting;
            if (runtime.room && runtime.connectedAreaKey === areaKey) return true;
        }

        runtime.connecting = (async () => {
            const client = liveKitClient();
            if (!client?.Room) return false;

            await disconnectRoom();

            const payload = await postLiveKitToken(areaKey, areaTitle || areaKey);
            const room = new client.Room({
                adaptiveStream: true,
                dynacast: true
            });
            wireLiveKitRoom(room);

            await room.connect(payload.url, payload.token, { autoSubscribe: true });
            runtime.room = room;
            runtime.connectedAreaKey = areaKey;
            rememberExistingRemoteTracks(room);
            await syncDesiredMicrophone();
            return true;
        })();

        try {
            return await runtime.connecting;
        } catch {
            await disconnectRoom();
            return false;
        } finally {
            runtime.connecting = null;
        }
    }

    async function fetchAudioState() {
        const response = await fetch(stateUrl, {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'Accept': 'application/json' }
        });

        if (response.status === 401 || response.status === 403) {
            releaseLeadership();
            await disconnectRoom();
            return null;
        }

        return await response.json().catch(() => null);
    }

    async function pollAudioState() {
        if (!runtime.isLeader || activeMeetingRoomOwner()) {
            await disconnectRoom();
            return;
        }

        const payload = await fetchAudioState();
        if (!payload?.ok || payload.enabled === false || payload.liveKitConfigured === false) {
            await disconnectRoom();
            return;
        }

        runtime.lastState = payload;
        const areaKey = normalizeAreaKey(payload.areaKey || payload.areaTitle);
        await connectRoom(areaKey, payload.areaTitle || areaKey);
    }

    function schedulePoll(delay = 300) {
        window.clearTimeout(runtime.pollTimer);
        runtime.pollTimer = window.setTimeout(() => {
            void pollAudioState();
        }, delay);
    }

    async function startHub() {
        if (!window.signalR || !hubUrl) return;

        runtime.hub = new signalR.HubConnectionBuilder()
            .withUrl(`${hubUrl}?userId=${currentUserId}`)
            .withAutomaticReconnect([0, 1500, 5000, 10000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        runtime.hub.on('VoiceAreaChanged', state => {
            if (!state || Number(state.userId || 0) === currentUserId) {
                schedulePoll(100);
            }
        });
        runtime.hub.on('PersonMoved', state => {
            if (!state || Number(state.userId || 0) === currentUserId) {
                schedulePoll(100);
            }
        });
        runtime.hub.on('PersonUpdated', state => {
            if (!state || Number(state.userId || 0) === currentUserId) {
                schedulePoll(100);
            }
        });
        runtime.hub.on('AreaSaved', () => schedulePoll(250));
        runtime.hub.on('AreaDeleted', () => schedulePoll(250));
        runtime.hub.onreconnected(() => schedulePoll(100));

        try {
            await runtime.hub.start();
        } catch {
            runtime.hub = null;
        }
    }

    function startLeadershipLoop() {
        const tick = async () => {
            const wasLeader = runtime.isLeader;
            const hasLeadership = acquireLeadership();
            if (hasLeadership) {
                renewLayoutOwner();
                if (!wasLeader) {
                    schedulePoll(50);
                }
            } else {
                await disconnectRoom();
            }
        };

        void tick();
        runtime.leaseTimer = window.setInterval(() => {
            void tick();
        }, 2500);
        runtime.ownerTimer = window.setInterval(() => {
            if (runtime.isLeader && !activeMeetingRoomOwner()) {
                renewLayoutOwner();
            }
        }, 2000);
        window.setInterval(() => {
            if (runtime.isLeader) {
                schedulePoll(50);
            }
        }, pollMs);
    }

    window.addEventListener('storage', event => {
        if (event.key === desiredMicKey) {
            void syncDesiredMicrophone();
        }

        if (event.key === audioOwnerKey || event.key === globalLeaderKey) {
            if (!acquireLeadership()) {
                void disconnectRoom();
            }
        }
    });

    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) {
            schedulePoll(100);
        }
    });

    window.addEventListener('beforeunload', () => {
        window.clearTimeout(runtime.pollTimer);
        window.clearInterval(runtime.leaseTimer);
        window.clearInterval(runtime.ownerTimer);
        releaseLeadership();
        runtime.hub?.stop?.();
        runtime.room?.disconnect?.();
    });

    void startHub();
    startLeadershipLoop();
})();
