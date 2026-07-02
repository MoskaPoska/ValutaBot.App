namespace ValutaBot.MiniApp;

public static class MiniAppUI
{
    public static string GetHtml()
    {
        return @"
<!DOCTYPE html>
<html lang='ru'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>
    <title>TradeBE бот — анализ рынка</title>
    <script src='https://telegram.org/js/telegram-web-app.js'></script>
    <style>
        @import url('https://fonts.googleapis.com/css2?family=Unbounded:wght@600;700;800;900&family=Inter:wght@400;600;700;800&display=swap');
        :root {
            --bg: #07051a;
            --panel: rgba(12, 10, 35, 0.88);
            --panel-border: rgba(124, 77, 255, 0.18);
            --accent: #7c4dff;
            --accent-glow: rgba(124, 77, 255, 0.35);
            --magenta: #b388ff;
            --cyan: #00e5ff;
            --gold: #ffd700;
            --green: #00e676;
            --red: #ff1744;
            --text: #ffffff;
            --subtext: #a89fd4;
            --dim: #5a5290;
            --radius: 18px;
            --btn-h: 56px;
            --glass-bg: rgba(255, 255, 255, 0.03);
        }
        * { box-sizing: border-box; margin: 0; padding: 0; -webkit-tap-highlight-color: transparent; }
        body {
            background: radial-gradient(ellipse at 50% -30%, #2d1060 0%, #0c0925 35%, #060412 70%, #03020a 100%);
            color: var(--text);
            padding: 20px;
            padding-bottom: 100px;
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
            user-select: none;
            font-size: 15px;
            line-height: 1.5;
            min-height: 100vh;
            overflow-x: hidden;
        }

        /* ─── Animated background particles ─── */
        body::before {
            content: '';
            position: fixed;
            inset: 0;
            background:
                radial-gradient(ellipse at 15% 60%, rgba(124,77,255,0.07) 0%, transparent 50%),
                radial-gradient(ellipse at 80% 20%, rgba(0,229,255,0.04) 0%, transparent 40%),
                radial-gradient(ellipse at 50% 80%, rgba(179,136,255,0.05) 0%, transparent 40%);
            pointer-events: none;
            z-index: 0;
        }

        .particle-field {
            position: fixed;
            inset: 0;
            pointer-events: none;
            z-index: 0;
            overflow: hidden;
        }
        .particle-field .p {
            position: absolute;
            width: 3px;
            height: 3px;
            border-radius: 50%;
            background: var(--magenta);
            opacity: 0;
            animation: floatUp 8s ease-in infinite;
        }
        .particle-field .p:nth-child(1) { left: 10%; width: 2px; height: 2px; background: var(--cyan); animation-delay: 0s; animation-duration: 9s; }
        .particle-field .p:nth-child(2) { left: 20%; width: 4px; height: 4px; background: var(--magenta); animation-delay: 1.2s; animation-duration: 7s; }
        .particle-field .p:nth-child(3) { left: 35%; width: 2px; height: 2px; background: #fff; animation-delay: 2.5s; animation-duration: 11s; }
        .particle-field .p:nth-child(4) { left: 50%; width: 3px; height: 3px; background: var(--gold); animation-delay: 0.8s; animation-duration: 8.5s; }
        .particle-field .p:nth-child(5) { left: 65%; width: 2px; height: 2px; background: var(--cyan); animation-delay: 3.2s; animation-duration: 10s; }
        .particle-field .p:nth-child(6) { left: 78%; width: 3px; height: 3px; background: var(--magenta); animation-delay: 1.8s; animation-duration: 7.5s; }
        .particle-field .p:nth-child(7) { left: 90%; width: 2px; height: 2px; background: #fff; animation-delay: 4s; animation-duration: 9.5s; }
        .particle-field .p:nth-child(8) { left: 5%; width: 3px; height: 3px; background: var(--gold); animation-delay: 5s; animation-duration: 12s; }
        .particle-field .p:nth-child(9) { left: 45%; width: 2px; height: 2px; background: var(--cyan); animation-delay: 2s; animation-duration: 8s; }
        .particle-field .p:nth-child(10) { left: 72%; width: 4px; height: 4px; background: var(--magenta); animation-delay: 3.5s; animation-duration: 10.5s; }
        .particle-field .p:nth-child(11) { left: 12%; width: 2px; height: 2px; background: #fff; animation-delay: 4.5s; animation-duration: 7s; }
        .particle-field .p:nth-child(12) { left: 55%; width: 3px; height: 3px; background: var(--gold); animation-delay: 0.5s; animation-duration: 9s; }
        .particle-field .p:nth-child(13) { left: 40%; width: 2px; height: 2px; background: var(--cyan); animation-delay: 6s; animation-duration: 11s; }
        .particle-field .p:nth-child(14) { left: 85%; width: 3px; height: 3px; background: var(--magenta); animation-delay: 2.2s; animation-duration: 8s; }
        .particle-field .p:nth-child(15) { left: 30%; width: 2px; height: 2px; background: #fff; animation-delay: 7s; animation-duration: 10s; }

        @keyframes floatUp {
            0% { transform: translateY(100vh) scale(0); opacity: 0; }
            10% { opacity: 1; }
            90% { opacity: 1; }
            100% { transform: translateY(-20vh) scale(1); opacity: 0; }
        }

        /* ─── Welcome Section ─── */
        .welcome-section {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin: 0 0 16px;
            background: linear-gradient(135deg, rgba(124,77,255,0.1) 0%, rgba(0,229,255,0.03) 70%, transparent 100%);
            border-radius: 18px;
            padding: 8px 14px;
            border: 1px solid rgba(124,77,255,0.12);
            transition: all 0.4s ease;
            position: relative;
            overflow: hidden;
        }
        .welcome-section::before {
            content: '';
            position: absolute;
            top: 0; left: 0; right: 0;
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(179,136,255,0.3), transparent);
        }
        .welcome-section.compact { margin-bottom: 0; opacity: 0.5; }
        .welcome-title {
            font-family: 'Unbounded', sans-serif;
            font-size: 15px;
            font-weight: 800;
            line-height: 1.3;
            max-width: 60%;
            background: linear-gradient(135deg, #e0d0ff 0%, #b388ff 30%, #7c4dff 60%, #00e5ff 100%);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            background-clip: text;
        }

        /* ─── 3D Magic Ball + Rings + Pedestal ─── */
        .sphere-container {
            position: relative;
            width: 120px;
            height: 142px;
            flex-shrink: 0;
            perspective: 600px;
            perspective-origin: 50% 45%;
            overflow: visible;
        }
        .sphere-scene {
            position: absolute;
            top: 2px;
            left: 50%;
            transform: translateX(-50%);
            width: 102px;
            height: 102px;
            transform-style: preserve-3d;
            overflow: visible;
            animation: floatBall 4s ease-in-out infinite;
        }
        .magic-ball {
            position: absolute;
            top: 1px;
            left: 1px;
            width: 100px;
            height: 100px;
            z-index: 2;
            will-change: transform;
            border-radius: 50%;
            background:
                repeating-linear-gradient(85deg,
                    transparent 0px, transparent 5px,
                    rgba(200,180,255,0.10) 5px, rgba(200,180,255,0.10) 6px
                ),
                repeating-radial-gradient(circle at 50% 50%,
                    transparent 0px, transparent 7px,
                    rgba(200,180,255,0.08) 7px, rgba(200,180,255,0.08) 8px
                ),
                radial-gradient(circle at 30% 35%, rgba(240,215,255,0.45) 0%, rgba(180,120,255,0.3) 20%, rgba(100,40,200,0.5) 45%, rgba(50,10,120,0.75) 70%, rgba(10,0,40,0.95) 100%);
            box-shadow:
                inset -10px 25px 45px rgba(255,255,255,0.08),
                inset 15px -25px 50px rgba(80,20,180,0.35),
                inset 0 -40px 50px rgba(0,0,0,0.35),
                0 0 65px rgba(138,43,226,0.35);
            animation: ball3dSpin 18s linear infinite;
        }
        .sphere-container.analyzing .magic-ball {
            animation: ball3dSpin 12s linear infinite, pulseGlow 1.2s infinite ease-in-out;
        }
        @keyframes pulseGlow {
            0% { box-shadow: inset -5px 20px 30px rgba(255,255,255,0.12), inset 15px -25px 50px rgba(80,20,180,0.45), inset 0 -35px 45px rgba(0,0,0,0.35), 0 0 55px rgba(138,43,226,0.5); }
            50% { box-shadow: inset -5px 20px 30px rgba(255,255,255,0.2), inset 15px -25px 50px rgba(179,136,255,0.5), inset 0 -35px 45px rgba(0,0,0,0.35), 0 0 85px rgba(179,136,255,0.8); }
            100% { box-shadow: inset -5px 20px 30px rgba(255,255,255,0.12), inset 15px -25px 50px rgba(80,20,180,0.45), inset 0 -35px 45px rgba(0,0,0,0.35), 0 0 55px rgba(138,43,226,0.5); }
        }
        @keyframes ball3dSpin {
            0% { transform: rotateY(0deg) rotateX(3deg); }
            100% { transform: rotateY(360deg) rotateX(3deg); }
        }
        @keyframes floatBall {
            0%, 100% { transform: translateX(-50%) translateY(0); }
            50% { transform: translateX(-50%) translateY(-6px); }
        }

        /* Glare / specular reflection overlay */
        .ball-glare {
            position: absolute;
            top: 5%;
            left: 12%;
            width: 40px;
            height: 22px;
            border-radius: 50%;
            background: radial-gradient(ellipse at center, rgba(255,255,255,0.6) 0%, rgba(255,255,255,0) 70%);
            transform: rotate(-25deg);
            pointer-events: none;
            z-index: 3;
        }
        .sphere-container.analyzing .ball-glare {
            animation: glarePulse 1.2s infinite ease-in-out;
        }
        .ball-glare-2 {
            position: absolute;
            bottom: 18%;
            right: 15%;
            width: 18px;
            height: 8px;
            border-radius: 50%;
            background: radial-gradient(ellipse at center, rgba(255,255,255,0.2) 0%, rgba(255,255,255,0) 70%);
            transform: rotate(20deg);
            pointer-events: none;
            z-index: 3;
        }
        @keyframes glarePulse {
            0% { opacity: 1; }
            50% { opacity: 1.4; }
            100% { opacity: 1; }
        }

        /* Spherical surface arcs — create 3D depth on rotation */
        .ball-line {
            position: absolute;
            border-radius: 50%;
            border: 1px solid rgba(200,170,255,0.18);
            pointer-events: none;
            left: 50%;
            top: 50%;
            transform: translate(-50%, -50%);
            z-index: 2;
            backface-visibility: hidden;
        }
        .lh1 { width: 96px; height: 20px; }
        .lh2 { width: 98px; height: 44px; }
        .lv1 { width: 20px; height: 96px; }
        .lv2 { width: 44px; height: 98px; }
        .sphere-container.analyzing .ball-line { border-color: rgba(200,170,255,0.35); }

        /* Inner arrow icon */
        .ball-arrow {
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            width: 46px;
            height: 46px;
            color: #df9aff;
            filter: drop-shadow(0 0 10px #c300ff) drop-shadow(0 0 25px #9d00ff);
            z-index: 4;
            pointer-events: none;
            opacity: 0.9;
        }

        /* Orbital Rings — 3 orbits at different angles & speeds */
        .orbits {
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            transform-style: preserve-3d;
            width: 0;
            height: 0;
            z-index: 1;
        }
        .orbit {
            position: absolute;
            top: 50%;
            left: 50%;
            width: 220px;
            height: 220px;
            margin-top: -110px;
            margin-left: -110px;
            border-radius: 50%;
            border: 1.5px solid rgba(170, 80, 255, 0.4);
            box-shadow: 0 0 15px rgba(170, 80, 255, 0.3), inset 0 0 15px rgba(170, 80, 255, 0.3);
            box-sizing: border-box;
            pointer-events: none;
            backface-visibility: hidden;
        }
        .orbit.o1 {
            animation: orbitSpin1 10s linear infinite;
        }
        .orbit.o2 {
            width: 180px;
            height: 180px;
            margin-top: -90px;
            margin-left: -90px;
            border-color: rgba(100, 200, 255, 0.35);
            box-shadow: 0 0 10px rgba(100, 200, 255, 0.25), inset 0 0 10px rgba(100, 200, 255, 0.25);
            animation: orbitSpin2 14s linear infinite;
        }
        .orbit.o3 {
            width: 215px;
            height: 215px;
            margin-top: -107.5px;
            margin-left: -107.5px;
            border-color: rgba(255, 100, 200, 0.25);
            box-shadow: 0 0 8px rgba(255, 100, 200, 0.2), inset 0 0 8px rgba(255, 100, 200, 0.2);
            animation: orbitSpin3 12s linear infinite;
        }

        @keyframes orbitSpin1 {
            0% { transform: rotateX(65deg) rotateY(25deg) rotateZ(0deg); }
            100% { transform: rotateX(65deg) rotateY(25deg) rotateZ(360deg); }
        }
        @keyframes orbitSpin2 {
            0% { transform: rotateX(75deg) rotateY(-30deg) rotateZ(0deg); }
            100% { transform: rotateX(75deg) rotateY(-30deg) rotateZ(-360deg); }
        }
        @keyframes orbitSpin3 {
            0% { transform: rotateX(50deg) rotateY(60deg) rotateZ(0deg); }
            100% { transform: rotateX(50deg) rotateY(60deg) rotateZ(360deg); }
        }

        /* Analyzing glow intensify */
        .sphere-container.analyzing .orbit.o1 {
            border-color: rgba(170, 80, 255, 0.8);
            box-shadow: 0 0 25px rgba(170, 80, 255, 0.5), inset 0 0 25px rgba(170, 80, 255, 0.5);
            animation-duration: 7s;
        }
        .sphere-container.analyzing .orbit.o2 {
            border-color: rgba(100, 200, 255, 0.7);
            box-shadow: 0 0 20px rgba(100, 200, 255, 0.4), inset 0 0 20px rgba(100, 200, 255, 0.4);
            animation-duration: 10s;
        }
        .sphere-container.analyzing .orbit.o3 {
            border-color: rgba(255, 100, 200, 0.6);
            box-shadow: 0 0 16px rgba(255, 100, 200, 0.35), inset 0 0 16px rgba(255, 100, 200, 0.35);
            animation-duration: 8s;
        }
            animation-duration: 3.5s;
        }
        .sphere-container.analyzing .orbit-ring.r5 {
            border-color: rgba(179, 136, 255, 0.4);
            box-shadow: 0 0 12px rgba(179, 136, 255, 0.1), inset 0 0 12px rgba(179, 136, 255, 0.05);
            animation-duration: 10s;
        }

        /* Pedestal — 3-layer hi-tech stand */
        .base-stand {
            position: absolute;
            bottom: 8px;
            left: 50%;
            transform: translateX(-50%);
            display: flex;
            flex-direction: column;
            align-items: center;
            z-index: 1;
        }
        .base-top {
            width: 70px;
            height: 12px;
            background: linear-gradient(to right, #2a1154, #48238c, #2a1154);
            border-radius: 50%;
            border: 1px solid rgba(107,59,196,0.4);
            box-shadow: 0 0 12px rgba(138,43,226,0.2);
        }
        .base-mid {
            width: 80px;
            height: 20px;
            background: linear-gradient(to right, #1d0940, #36176e, #1d0940);
            border-radius: 10px;
            margin-top: -6px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.5);
            border: 1px solid rgba(74,33,150,0.3);
        }
        .base-bot {
            width: 90px;
            height: 14px;
            background: linear-gradient(to right, #11042b, #240d4f, #11042b);
            border-radius: 50%;
            margin-top: -7px;
            box-shadow: 0 6px 20px rgba(0,0,0,0.6);
            border: 1px solid rgba(74,33,150,0.15);
        }

        /* ─── Ornate divider ─── */
        .magic-divider {
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(124,77,255,0.25), transparent);
            margin: 14px 0;
            position: relative;
        }
        .magic-divider::after {
            content: '✦';
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            font-size: 9px;
            color: rgba(179,136,255,0.35);
            background: var(--bg);
            padding: 0 10px;
        }

        /* ─── Market Bar (F&G + ticker) ─── */
        .market-bar {
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 8px 12px;
            margin-bottom: 14px;
            background: linear-gradient(135deg, rgba(124,77,255,0.03), rgba(0,229,255,0.01));
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border: 1px solid rgba(124,77,255,0.08);
            border-radius: 14px;
            font-size: 11px;
        }
        .mb-item { display: flex; align-items: center; gap: 5px; }
        .mb-label { color: var(--dim); font-weight: 600; }
        .mb-price { color: var(--text); font-weight: 700; font-family: 'Unbounded', sans-serif; font-size: 10px; }
        .mb-divider { width: 1px; height: 16px; background: rgba(124,77,255,0.12); flex-shrink: 0; }
        .mb-fng { margin-left: auto; font-weight: 700; font-size: 12px; white-space: nowrap; }

        /* ─── Top Categories ─── */
        .top-categories {
            display: flex;
            background: linear-gradient(135deg, rgba(124,77,255,0.04), rgba(0,229,255,0.02));
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border: 1px solid rgba(124,77,255,0.12);
            border-radius: 16px;
            padding: 6px;
            margin-bottom: 20px;
            position: relative;
            box-shadow:
                0 0 0 1px rgba(124,77,255,0.06),
                inset 0 1px 0 rgba(255,255,255,0.03),
                0 4px 20px rgba(0,0,0,0.3);
        }
        .top-categories .cat-divider {
            width: 1px;
            align-self: stretch;
            margin: 8px 0;
            background: linear-gradient(180deg, transparent, rgba(124,77,255,0.35), rgba(0,229,255,0.18), rgba(124,77,255,0.35), transparent);
            flex-shrink: 0;
        }
        .top-cat-btn {
            flex: 1;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            padding: 10px 0 8px;
            border-radius: 12px;
            cursor: pointer;
            color: var(--dim);
            transition: all 0.35s cubic-bezier(0.34, 1.56, 0.64, 1);
            position: relative;
        }
        .top-cat-btn::before {
            content: '';
            position: absolute;
            inset: 0;
            background: linear-gradient(135deg, var(--accent), #00bcd4);
            opacity: 0;
            transition: opacity 0.35s;
            border-radius: 12px;
        }
        .top-cat-btn.active::before { opacity: 1; }
        .top-cat-btn.active::after {
            content: '';
            position: absolute;
            inset: -1px;
            border-radius: 13px;
            border: 1px solid rgba(124,77,255,0.3);
        }
        .top-cat-btn svg { width: 22px; height: 22px; opacity: 0.5; transition: 0.35s; position: relative; z-index: 1; }
        .top-cat-btn .label { font-size: 10px; font-weight: 700; margin-top: 5px; letter-spacing: 0.3px; position: relative; z-index: 1; }
        .top-cat-btn.active { color: #fff; box-shadow: 0 4px 20px var(--accent-glow); }
        .top-cat-btn.active svg { opacity: 1; filter: drop-shadow(0 0 6px rgba(255,255,255,0.3)); }
        .top-cat-btn.active svg path, .top-cat-btn.active svg circle { stroke: #fff; }

        /* ─── Selector Section ─── */
        .selector-section {
            background: linear-gradient(135deg, rgba(124,77,255,0.04), transparent 70%);
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border: 1px solid rgba(124,77,255,0.12);
            border-radius: 24px;
            padding: 24px 22px;
            margin-bottom: 20px;
            position: relative;
            z-index: 50;
            box-shadow:
                0 0 0 1px rgba(124,77,255,0.06),
                0 8px 48px rgba(124,77,255,0.06),
                inset 0 1px 0 rgba(255,255,255,0.04);
        }
        .sel-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 14px;
            margin-bottom: 16px;
            position: relative;
        }
        .sel-grid::after {
            content: '';
            position: absolute;
            left: 50%;
            top: 8px;
            bottom: 8px;
            width: 1px;
            background: linear-gradient(180deg, transparent, rgba(124,77,255,0.25), rgba(0,229,255,0.14), rgba(124,77,255,0.25), transparent);
            transform: translateX(-50%);
            pointer-events: none;
        }
        .sel-label { font-size: 10px; color: var(--dim); text-transform: uppercase; letter-spacing: 1.2px; margin-bottom: 10px; font-weight: 700; display: block; }
        .dropdown-trigger {
            background: rgba(255,255,255,0.03);
            border: 1px solid var(--panel-border);
            height: var(--btn-h);
            padding: 0 18px;
            border-radius: var(--radius);
            display: flex;
            justify-content: space-between;
            align-items: center;
            cursor: pointer;
            transition: all 0.25s;
            position: relative;
        }
        .dropdown-trigger:hover { border-color: rgba(124,77,255,0.3); background: rgba(124,77,255,0.05); }
        .dropdown-val { font-size: 14px; font-weight: 700; color: #fff; }
        .dropdown-arrow { font-size: 10px; color: var(--dim); }

        .asset-menu, .tf-menu {
            display: none;
            position: absolute;
            top: calc(100% + 8px);
            left: 0;
            width: 100%;
            background: rgba(8, 6, 24, 0.97);
            backdrop-filter: blur(30px);
            -webkit-backdrop-filter: blur(30px);
            border: 1px solid var(--panel-border);
            border-radius: 20px;
            padding: 18px;
            z-index: 100;
            box-shadow: 0 20px 60px rgba(0,0,0,0.9);
            animation: menuIn 0.2s ease-out;
        }
        @keyframes menuIn { from { opacity: 0; transform: translateY(-8px); } to { opacity: 1; transform: translateY(0); } }
        .asset-menu.show, .tf-menu.show { display: block; }
        .asset-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
        .asset-item {
            background: rgba(255,255,255,0.03);
            color: var(--subtext);
            padding: 13px 0;
            text-align: center;
            border-radius: 12px;
            font-size: 12px;
            font-weight: 700;
            cursor: pointer;
            margin-bottom: 6px;
            border: 1px solid transparent;
            transition: all 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
        }
        .asset-item:hover { background: rgba(124,77,255,0.08); border-color: rgba(124,77,255,0.2); }
        .asset-item.active { background: linear-gradient(135deg, var(--accent), #6a3de8); color: #fff; border-color: var(--accent); box-shadow: 0 4px 20px var(--accent-glow); }

        .otc-scroll { max-height: 340px; overflow-y: auto; scrollbar-width: thin; scrollbar-color: rgba(124,77,255,0.3) transparent; scroll-behavior: smooth; }
        .otc-scroll::-webkit-scrollbar { width: 3px; }
        .otc-scroll::-webkit-scrollbar-track { background: transparent; }
        .otc-scroll::-webkit-scrollbar-thumb { background: rgba(124,77,255,0.3); border-radius: 3px; }

        .tf-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; }
        .tf-btn {
            background: rgba(255,255,255,0.03);
            border: 1px solid var(--panel-border);
            height: 48px;
            color: var(--subtext);
            border-radius: 12px;
            cursor: pointer;
            font-size: 13px;
            font-weight: 700;
            transition: all 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
        }
        .tf-btn:hover { background: rgba(124,77,255,0.08); border-color: rgba(124,77,255,0.2); }
        .tf-btn.active { background: linear-gradient(135deg, var(--accent), #6a3de8); color: #fff; border-color: var(--accent); box-shadow: 0 4px 20px var(--accent-glow); }

        /* ─── Analyze Button ─── */
        .btn-analyze {
            width: 100%;
            height: var(--btn-h);
            background: linear-gradient(135deg, #7c4dff 0%, #b388ff 30%, #00e5ff 100%);
            background-size: 200% 200%;
            border: none;
            color: white;
            border-radius: var(--radius);
            font-weight: 800;
            font-size: 14px;
            letter-spacing: 2px;
            text-transform: uppercase;
            cursor: pointer;
            margin-top: 10px;
            transition: all 0.35s cubic-bezier(0.34, 1.56, 0.64, 1);
            box-shadow: 0 6px 30px rgba(124, 77, 255, 0.3);
            position: relative;
            overflow: hidden;
            animation: btnShimmer 4s ease-in-out infinite;
        }
        @keyframes btnShimmer {
            0% { background-position: 0% 50%; }
            50% { background-position: 100% 50%; }
            100% { background-position: 0% 50%; }
        }
        .btn-analyze::after {
            content: '';
            position: absolute;
            inset: 0;
            background: linear-gradient(135deg, transparent 40%, rgba(255,255,255,0.12) 60%, transparent 80%);
            transition: transform 0.6s;
            transform: translateX(-100%);
        }
        .btn-analyze:hover::after { transform: translateX(100%); }
        .btn-analyze:active { transform: scale(0.96); }
        .btn-analyze:disabled { opacity: 0.5; transform: none; box-shadow: none; animation: none; }

        /* ─── Results ─── */
        .results-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; margin-top: 14px; }
        .res-card {
            background: linear-gradient(135deg, rgba(124,77,255,0.04), transparent);
            backdrop-filter: blur(20px);
            -webkit-backdrop-filter: blur(20px);
            border: 1px solid var(--panel-border);
            border-radius: 20px;
            padding: 18px 6px 14px;
            text-align: center;
            min-height: 148px;
            display: flex;
            flex-direction: column;
            align-items: center;
            box-shadow: 0 4px 24px rgba(124, 77, 255, 0.04), inset 0 1px 0 rgba(255,255,255,0.04);
            transition: border-color 0.3s;
            position: relative;
            overflow: hidden;
        }
        .res-card::before {
            content: '';
            position: absolute;
            top: 0; left: 20%; right: 20%;
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(124,77,255,0.15), transparent);
        }
        .res-card:hover { border-color: rgba(124,77,255,0.2); }
        .res-label { font-size: 9px; color: var(--dim); text-transform: uppercase; letter-spacing: 1.2px; font-weight: 700; margin-bottom: 10px; }
        .res-value { font-size: 20px; font-weight: 800; font-family: 'Unbounded', sans-serif; }
        .res-chart { margin-top: 6px; width: 100%; height: 48px; display: flex; align-items: flex-end; justify-content: center; gap: 3px; }
        .res-chart-bar {
            width: 6px;
            border-radius: 3px 3px 0 0;
            background: var(--accent);
            transition: height 0.6s cubic-bezier(0.34, 1.56, 0.64, 1);
        }
        .res-chart-bar.green { background: var(--green); }
        .res-chart-bar.red { background: var(--red); }
        .res-dir-chart { margin-top: 4px; width: 100%; height: 44px; }
        .res-dir-chart svg { width: 100%; height: 100%; }

        /* ─── Levels ─── */
        .levels-bar {
            display: none;
            background: linear-gradient(135deg, rgba(124,77,255,0.06), rgba(0,188,212,0.03));
            backdrop-filter: blur(20px);
            border: 1px solid var(--panel-border);
            border-radius: 18px;
            padding: 10px 14px;
            margin-top: 12px;
            animation: slideUp 0.35s ease-out;
            position: relative;
        }
        .levels-bar::before {
            content: '';
            position: absolute;
            top: -1px; left: 20%; right: 20%;
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(124,77,255,0.15), transparent);
        }
        @keyframes slideUp { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: translateY(0); } }
        .level-line { display: flex; align-items: center; padding: 4px 0; gap: 8px; font-size: 11px; }
        .level-line .tag { font-size: 8px; font-weight: 800; padding: 2px 6px; border-radius: 4px; text-transform: uppercase; min-width: 22px; text-align: center; }
        .level-line .tag.l1 { background: rgba(124,77,255,0.2); color: #b388ff; }
        .level-line .tag.l2 { background: rgba(0,229,255,0.15); color: #00e5ff; }
        .level-line .tag.l3 { background: rgba(255,214,0,0.15); color: #ffd600; }
        .level-line .info { flex: 1; color: var(--subtext); }
        .level-line .result { font-weight: 800; font-size: 10px; padding: 2px 8px; border-radius: 5px; text-transform: uppercase; min-width: 52px; text-align: center; }
        .level-line .result.buy { background: rgba(0,230,118,0.12); color: var(--green); }
        .level-line .result.put { background: rgba(255,23,68,0.12); color: var(--red); }
        .level-line .result.neutral { background: rgba(255,255,255,0.04); color: var(--dim); }
        .levels-divider {
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(124,77,255,0.12), transparent);
            margin: 6px 0;
            position: relative;
        }
        .levels-divider::after {
            content: '◆';
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            font-size: 6px;
            color: rgba(124,77,255,0.2);
            background: var(--bg);
            padding: 0 6px;
        }
        .levels-total { display: flex; align-items: center; justify-content: space-between; padding: 4px 2px 0; font-size: 11px; font-weight: 700; }
        .levels-total .dir { font-size: 12px; padding: 3px 12px; border-radius: 6px; text-transform: uppercase; }
        .levels-total .dir.buy { background: rgba(0,230,118,0.12); color: var(--green); }
        .levels-total .dir.put { background: rgba(255,23,68,0.12); color: var(--red); }

        @keyframes resultFlash {
            0% { border-color: var(--accent); box-shadow: 0 0 35px var(--accent-glow), inset 0 0 25px rgba(124,77,255,0.08); }
            50% { border-color: #b388ff; box-shadow: 0 0 55px rgba(124,77,255,0.5), inset 0 0 35px rgba(124,77,255,0.12); }
            100% { border-color: var(--panel-border); box-shadow: 0 4px 24px rgba(124, 77, 255, 0.04), inset 0 1px 0 rgba(255,255,255,0.04); }
        }
        .res-card.flash { animation: resultFlash 0.7s ease-out; }

        /* ─── ML Forecast Card ─── */
        .ml-card {
            margin-top: 12px;
            background: linear-gradient(135deg, rgba(124,77,255,0.05), rgba(0,229,255,0.02));
            border: 1px solid rgba(124,77,255,0.12);
            border-radius: 16px;
            padding: 14px 18px;
            display: flex;
            align-items: center;
            justify-content: space-between;
        }
        .ml-header { display: flex; align-items: center; gap: 10px; }
        .ml-badge { font-size: 11px; font-weight: 800; padding: 3px 8px; border-radius: 6px; background: rgba(124,77,255,0.15); color: #b388ff; }
        .ml-label { font-size: 10px; color: var(--dim); text-transform: uppercase; letter-spacing: 0.8px; font-weight: 600; }
        .ml-body { display: flex; align-items: center; gap: 12px; }
        .ml-dir { font-size: 16px; font-weight: 800; font-family: 'Unbounded', sans-serif; }
        .ml-conf { font-size: 14px; font-weight: 800; font-family: 'Unbounded', sans-serif; color: var(--accent); }

        /* ─── News Card ─── */
        .news-card {
            margin-top: 12px;
            background: linear-gradient(135deg, rgba(0,229,255,0.04), rgba(124,77,255,0.02));
            border: 1px solid rgba(0,229,255,0.10);
            border-radius: 16px;
            padding: 14px 18px;
        }
        .news-header { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
        .news-badge { font-size: 11px; font-weight: 800; padding: 3px 8px; border-radius: 6px; background: rgba(0,229,255,0.12); color: #00e5ff; }
        .news-label { font-size: 10px; color: var(--dim); text-transform: uppercase; letter-spacing: 0.8px; font-weight: 600; flex: 1; }
        .news-sentiment { font-size: 13px; font-weight: 800; font-family: 'Unbounded', sans-serif; }
        .news-summary { font-size: 11px; color: var(--subtext); line-height: 1.5; }
        .news-headlines { margin-top: 10px; }
        .news-toggle { font-size: 10px; color: var(--accent); cursor: pointer; font-weight: 700; letter-spacing: 0.5px; text-transform: uppercase; user-select: none; }
        .news-toggle:hover { opacity: 0.8; }
        .news-list { margin-top: 8px; display: none; flex-direction: column; gap: 4px; }
        .news-list.open { display: flex; }
        .news-list-item { font-size: 10px; color: var(--dim); padding: 4px 8px; background: rgba(124,77,255,0.04); border-radius: 6px; line-height: 1.4; }

        /* ─── Alerts Panel ─── */
        .alert-section {
            margin-top: 16px;
            background: linear-gradient(135deg, rgba(124,77,255,0.03), rgba(0,229,255,0.01));
            border: 1px solid rgba(124,77,255,0.08);
            border-radius: 16px;
            overflow: hidden;
        }
        .alert-toggle {
            display: flex; justify-content: space-between; align-items: center;
            padding: 14px 16px; cursor: pointer; font-size: 13px; font-weight: 700; color: var(--text);
        }
        .alert-toggle:hover { background: rgba(124,77,255,0.04); }
        .alert-arrow { transition: transform 0.3s; font-size: 10px; color: var(--dim); }
        .alert-arrow.open { transform: rotate(90deg); }
        .alert-body { padding: 0 16px 16px; }
        .alert-form { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px; }
        .alert-input {
            background: rgba(124,77,255,0.08); border: 1px solid rgba(124,77,255,0.12);
            border-radius: 8px; padding: 6px 10px; color: var(--text); font-size: 11px;
            font-family: inherit; outline: none;
        }
        .alert-input:focus { border-color: var(--accent); }
        .alert-input select { min-width: 80px; }
        .alert-input[type=number] { width: 50px; }
        .alert-btn {
            background: var(--accent); border: none; border-radius: 8px;
            padding: 6px 14px; color: #fff; font-weight: 700; font-size: 14px;
            cursor: pointer; transition: 0.2s;
        }
        .alert-btn:hover { filter: brightness(1.2); }
        .alert-list { display: flex; flex-direction: column; gap: 6px; }
        .alert-item {
            display: flex; justify-content: space-between; align-items: center;
            padding: 8px 12px; background: rgba(124,77,255,0.04);
            border: 1px solid rgba(124,77,255,0.06); border-radius: 8px;
            font-size: 11px;
        }
        .alert-item .del { cursor: pointer; color: var(--red); font-weight: 700; font-size: 14px; opacity: 0.6; }
        .alert-item .del:hover { opacity: 1; }

        /* ─── Candle Countdown ─── */
        .candle-countdown {
            background: linear-gradient(90deg, rgba(124,77,255,0.04), rgba(0,229,255,0.02));
            border: 1px solid var(--panel-border);
            border-radius: 12px;
            padding: 10px 14px;
            margin-top: 12px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            position: relative;
        }
        .candle-countdown::before {
            content: '';
            position: absolute;
            top: -1px; left: 10%; right: 10%;
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(124,77,255,0.15), transparent);
        }
        .candle-countdown .label { font-size: 10px; color: var(--dim); text-transform: uppercase; letter-spacing: 0.8px; font-weight: 700; }
        .candle-countdown .time { font-size: 18px; font-weight: 800; font-family: 'Unbounded', sans-serif; color: var(--accent); min-width: 56px; text-align: right; letter-spacing: 1px; }
        .candle-countdown .time.warning { color: #ffd600; }
        .candle-countdown .time.critical { color: #ff1744; }

        /* ─── Status Bar ─── */
        .status-bar {
            display: none;
            background: linear-gradient(135deg, rgba(124,77,255,0.06), rgba(0,188,212,0.03));
            backdrop-filter: blur(16px);
            -webkit-backdrop-filter: blur(16px);
            border: 1px solid var(--panel-border);
            border-radius: 14px;
            padding: 10px 14px;
            margin-top: 10px;
            align-items: center;
            gap: 12px;
            animation: sbIn 0.3s ease;
            pointer-events: auto;
            position: relative;
        }
        .status-bar.show { display: flex; }
        @keyframes sbIn { from { opacity: 0; transform: translateY(-6px); } to { opacity: 1; transform: translateY(0); } }
        .status-bar .sb-text { flex: 1; min-width: 0; }
        .status-bar .sb-title { font-size: 12px; font-weight: 700; color: var(--accent); letter-spacing: 1px; }
        .status-bar .sb-title .blink { animation: sbBlink 0.8s step-end infinite; }
        @keyframes sbBlink { 50% { opacity: 0; } }
        .status-bar .sb-sub { font-size: 10px; color: var(--dim); margin-top: 1px; font-weight: 600; letter-spacing: 0.5px; transition: opacity 0.25s; }
        .status-bar .sb-sub.fade { opacity: 0.3; }
        .status-bar .sb-bars { display: flex; gap: 3px; align-items: flex-end; height: 18px; flex-shrink: 0; }
        .status-bar .sb-bars .sbb {
            width: 3px; border-radius: 2px;
            background: linear-gradient(to top, var(--accent), #b388ff);
            animation: sbbWave 0.7s ease-in-out infinite alternate;
        }
        .status-bar .sb-bars .sbb:nth-child(1) { height: 4px; animation-delay: 0s; }
        .status-bar .sb-bars .sbb:nth-child(2) { height: 8px; animation-delay: 0.1s; }
        .status-bar .sb-bars .sbb:nth-child(3) { height: 14px; animation-delay: 0.2s; }
        .status-bar .sb-bars .sbb:nth-child(4) { height: 10px; animation-delay: 0.3s; }
        .status-bar .sb-bars .sbb:nth-child(5) { height: 6px; animation-delay: 0.4s; }
        @keyframes sbbWave { 0% { transform: scaleY(0.5); opacity: 0.4; } 100% { transform: scaleY(1); opacity: 1; } }

        /* ─── Navigation ─── */
        .nav-bar {
            position: fixed;
            bottom: 0;
            left: 0;
            width: 100%;
            background: rgba(7, 5, 26, 0.94);
            backdrop-filter: blur(30px);
            -webkit-backdrop-filter: blur(30px);
            border-top: 1px solid var(--panel-border);
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            padding: 14px 0;
            padding-bottom: max(14px, env(safe-area-inset-bottom));
            z-index: 10;
        }
        .nav-bar::before {
            content: '';
            position: absolute;
            top: -1px;
            left: 20%;
            right: 20%;
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(124,77,255,0.2), transparent);
        }
        .nav-item {
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            gap: 5px;
            font-weight: 600;
            font-size: 11px;
            cursor: pointer;
            color: var(--dim);
            transition: all 0.3s;
            position: relative;
        }
        .nav-item.active { color: #fff; }
        .nav-item svg { width: 22px; height: 22px; opacity: 0.45; transition: 0.3s; }
        .nav-item.active svg { opacity: 1; filter: drop-shadow(0 0 10px rgba(124,77,255,0.5)); }
        .nav-item.active svg path { stroke: #b388ff; }
        .nav-item.active::before {
            content: '';
            position: absolute;
            top: -1px;
            width: 50px;
            height: 2px;
            background: linear-gradient(90deg, transparent, var(--accent), var(--cyan), transparent);
            border-radius: 2px;
            filter: drop-shadow(0 0 4px rgba(124,77,255,0.4));
        }

        .app-screen { display: none; }
        .app-screen.active { display: block; }

        /* ─── History ─── */
        .hist-header {
            font-family: 'Unbounded', sans-serif;
            font-size: 16px;
            font-weight: 800;
            margin-bottom: 12px;
            background: linear-gradient(135deg, #e0d0ff, #b388ff);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            background-clip: text;
        }
        .filter-row { display: flex; gap: 6px; margin-bottom: 14px; }
        .filter-btn {
            flex: 1; background: rgba(255,255,255,0.03); border: 1px solid var(--panel-border);
            color: var(--dim); padding: 8px 0; border-radius: 10px;
            font-size: 10px; font-weight: 700; cursor: pointer; transition: all 0.25s;
            position: relative;
        }
        .filter-btn.active { background: rgba(124,77,255,0.12); border-color: var(--accent); color: #fff; box-shadow: 0 0 12px rgba(124,77,255,0.15); }
        .history-list { display: flex; flex-direction: column; gap: 6px; }
        .history-item {
            background: linear-gradient(135deg, rgba(124,77,255,0.03), transparent);
            border: 1px solid var(--panel-border);
            border-radius: 12px; padding: 10px 12px;
            display: flex; align-items: center; justify-content: space-between;
            position: relative;
        }
        .history-item::before {
            content: '';
            position: absolute;
            top: -1px; left: 10%; right: 10%;
            height: 1px;
            background: linear-gradient(90deg, transparent, rgba(124,77,255,0.1), transparent);
        }
        .hi-asset { font-size: 12px; font-weight: 700; }
        .hi-tf { font-size: 9px; color: var(--dim); font-weight: 600; }
        .hi-dir { font-size: 11px; font-weight: 800; padding: 2px 8px; border-radius: 5px; }
        .hi-dir.buy { background: rgba(0,230,118,0.1); color: var(--green); }
        .hi-dir.put { background: rgba(255,23,68,0.1); color: var(--red); }
        .hi-prob { font-size: 13px; font-weight: 800; }
        .hi-time { font-size: 9px; color: var(--dim); }
        .history-empty { text-align: center; padding: 30px 0; color: var(--dim); font-size: 13px; }

        /* ─── Top asset star ─── */
        .top-star { font-size: 13px; line-height: 1; color: var(--gold); margin-left: 5px; display: inline-block; filter: drop-shadow(0 0 4px rgba(255,215,0,0.5)); }

        /* ─── Liquidation Heatmap ─── */
        .heatmap-header {
            font-family: 'Unbounded', sans-serif;
            font-size: 16px;
            font-weight: 800;
            margin-bottom: 12px;
            background: linear-gradient(135deg, #ff6b6b, #ffd700);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            background-clip: text;
        }
        .heatmap-controls { display: flex; gap: 6px; margin-bottom: 14px; flex-wrap: wrap; }
        .heatmap-sym-btn {
            background: rgba(255,255,255,0.03); border: 1px solid var(--panel-border);
            color: var(--dim); padding: 8px 14px; border-radius: 10px;
            font-size: 11px; font-weight: 700; cursor: pointer; transition: all 0.25s;
        }
        .heatmap-sym-btn.active { background: rgba(255,107,107,0.12); border-color: #ff6b6b; color: #fff; }
        .heatmap-canvas-wrap {
            background: rgba(12,10,35,0.6); border: 1px solid var(--panel-border);
            border-radius: 18px; padding: 16px; position: relative;
        }
        .heatmap-canvas-wrap canvas { display: block; width: 100%; height: auto; }
        .heatmap-empty { text-align: center; padding: 40px 0; color: var(--dim); font-size: 13px; }
        .heatmap-legend { display: flex; align-items: center; gap: 16px; margin-top: 10px; font-size: 10px; color: var(--dim); }
        .heatmap-legend .bar { width: 20px; height: 8px; border-radius: 2px; display: inline-block; }
        .heatmap-legend .bar.long { background: var(--green); }
        .heatmap-legend .bar.short { background: var(--red); }
    </style>
</head>
<body>

    <!-- Animated floating particles -->
    <div class='particle-field'>
        <div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div>
        <div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div>
        <div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div><div class='p'></div>
    </div>

    <div id='screen-home' class='app-screen active'>

        <div class='welcome-section' id='welcomeSec'>
            <div class='welcome-title'>Приветствую<br>на голодных<br>играх</div>
            <div id='mainSphere' class='sphere-container'>
                <div class='sphere-scene'>
                    <div class='orbits'>
                        <div class='orbit o1'></div>
                        <div class='orbit o2'></div>
                        <div class='orbit o3'></div>
                    </div>
                    <div class='magic-ball'>
                        <div class='ball-line lh lh1'></div>
                        <div class='ball-line lh lh2'></div>
                        <div class='ball-line lv lv1'></div>
                        <div class='ball-line lv lv2'></div>
                        <div class='ball-glare'></div>
                        <div class='ball-glare-2'></div>
                        <div class='ball-arrow'>
                            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='3' stroke-linecap='round' stroke-linejoin='round' width='48' height='48'>
                                <polyline points='23 6 13.5 15.5 8.5 10.5 1 18'></polyline>
                                <polyline points='17 6 23 6 23 12'></polyline>
                            </svg>
                        </div>
                    </div>
                </div>
                <div class='base-stand'>
                    <div class='base-top'></div>
                    <div class='base-mid'></div>
                    <div class='base-bot'></div>
                </div>
            </div>
        </div>

        <div class='market-bar' id='marketBar'>
            <div class='mb-item'><span class='mb-label'>BTC</span><span class='mb-price' id='mbBtc'>--</span></div>
            <div class='mb-item'><span class='mb-label'>ETH</span><span class='mb-price' id='mbEth'>--</span></div>
            <div class='mb-item'><span class='mb-label'>SOL</span><span class='mb-price' id='mbSol'>--</span></div>
            <div class='mb-divider'></div>
            <div class='mb-fng' id='mbFng'>😐 50</div>
        </div>

        <div class='top-categories'>
            <div class='top-cat-btn active' data-cat='fiat' onclick='changeTopCategory(this)'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><circle cx='12' cy='12' r='9'/><path d='M7 9h6a2 2 0 0 1 0 4H7'/><path d='M10 5v2m0 8v2'/></svg>
                <div class='label'>Валюты</div>
            </div>
            <div class='cat-divider'></div>
            <div class='top-cat-btn' data-cat='commodities' onclick='changeTopCategory(this)'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><path d='M12 2a8 8 0 0 0-8 8c0 5 8 12 8 12s8-7 8-12a8 8 0 0 0-8-8z'/><circle cx='12' cy='10' r='3'/></svg>
                <div class='label'>Сырьё</div>
            </div>
            <div class='cat-divider'></div>
            <div class='top-cat-btn' data-cat='crypto' onclick='changeTopCategory(this)'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round'><path d='M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'/></svg>
                <div class='label'>Крипта</div>
            </div>
            <div class='cat-divider'></div>
            <div class='top-cat-btn' data-cat='stocks' onclick='changeTopCategory(this)'>
                <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><polyline points='22 7 13.5 15.5 8.5 10.5 2 17'/><polyline points='16 7 22 7 22 13'/></svg>
                <div class='label'>Акции</div>
            </div>
        </div>

        <div class='selector-section'>
            <div class='sel-grid'>
                <div class='sel-group'>
                    <span class='sel-label'>Актив</span>
                    <div class='dropdown-trigger' id='assetBtn' onclick='toggleMenu(`assetMenu`, `assetBtn`)'>
                        <span class='dropdown-val' id='selectedAsset'>EUR/USD OTC</span>
                        <span class='dropdown-arrow'>▼</span>
                    </div>
                </div>
                <div class='sel-group'>
                    <span class='sel-label'>Таймфрейм</span>
                    <div class='dropdown-trigger' id='tfBtn' onclick='toggleMenu(`tfMenu`, `tfBtn`)'>
                        <span class='dropdown-val' id='selectedTf'>M1</span>
                        <span class='dropdown-arrow'>▼</span>
                    </div>
                </div>
            </div>

            <div class='asset-menu' id='assetMenu'>
                <div class='asset-grid' id='assetGrid'></div>
            </div>

            <div class='tf-menu' id='tfMenu'>
                <div class='tf-grid'>
                    <button class='tf-btn' data-tf='S5' onclick='setTf(this)'>S5</button>
                    <button class='tf-btn' data-tf='S15' onclick='setTf(this)'>S15</button>
                    <button class='tf-btn' data-tf='S30' onclick='setTf(this)'>S30</button>
                    <button class='tf-btn' data-tf='S5' onclick='setTf(this)'>S5</button>
                    <button class='tf-btn' data-tf='S15' onclick='setTf(this)'>S15</button>
                    <button class='tf-btn' data-tf='S30' onclick='setTf(this)'>S30</button>
                    <button class='tf-btn active' data-tf='M1' onclick='setTf(this)'>M1</button>
                    <button class='tf-btn' data-tf='M2' onclick='setTf(this)'>M2</button>
                    <button class='tf-btn' data-tf='M3' onclick='setTf(this)'>M3</button>
                    <button class='tf-btn' data-tf='M5' onclick='setTf(this)'>M5</button>
                    <button class='tf-btn' data-tf='M15' onclick='setTf(this)'>M15</button>
                    <button class='tf-btn' data-tf='M30' onclick='setTf(this)'>M30</button>
                    <button class='tf-btn' data-tf='H1' onclick='setTf(this)'>H1</button>
                    <button class='tf-btn' data-tf='H4' onclick='setTf(this)'>H4</button>
                    <button class='tf-btn' data-tf='D1' onclick='setTf(this)'>D1</button>
                </div>
            </div>

            <div class='candle-countdown'>
                <span class='label'>До закрытия свечи</span>
                <span class='time' id='candleTime'>--</span>
            </div>

            <button class='btn-analyze' id='btnGet'>ПОЛУЧИТЬ АНАЛИЗ</button>

            <div class='status-bar' id='statusBar'>
                <div class='sb-text'>
                    <div class='sb-title' id='sbTitle'>АНАЛИЗИРУЮ РЫНОК<span class='blink'>.</span></div>
                    <div class='sb-sub' id='sbSub'>ЗАГРУЗКА ДАННЫХ</div>
                </div>
                <div class='sb-bars'><div class='sbb'></div><div class='sbb'></div><div class='sbb'></div><div class='sbb'></div><div class='sbb'></div></div>
            </div>
        </div>

        <div class='results-grid'>
            <div class='res-card'>
                <div class='res-label'>Вероятность</div>
                <div class='res-value' id='resProb' style='color:var(--accent)'>--%</div>
                <div class='res-chart' id='probChart'></div>
            </div>
            <div class='res-card'>
                <div class='res-label'>Направление</div>
                <div class='res-value' id='resDir' style='color:var(--subtext)'>--</div>
                <div class='res-dir-chart' id='dirChart'>
                    <svg viewBox='0 0 80 40'><path d='M10 35 L40 5 L70 35' stroke='var(--dim)' stroke-width='2.5' fill='none' stroke-linecap='round' stroke-linejoin='round' opacity='0.3'/></svg>
                </div>
            </div>
            <div class='res-card'>
                <div class='res-label'>Время</div>
                <div class='res-value' id='resDur' style='color:var(--subtext)'>--</div>
                <div class='res-chart' id='durChart'></div>
            </div>
            <div class='res-card'>
                <div class='res-label'>RSI</div>
                <div class='res-value' id='resRsi' style='color:var(--subtext);font-size:16px'>--</div>
                <div class='res-chart' id='rsiChart'></div>
            </div>
            <div class='res-card'>
                <div class='res-label'>EMA (9)</div>
                <div class='res-value' id='resEma' style='color:var(--subtext);font-size:16px'>--</div>
                <div class='res-chart' id='emaChart'></div>
            </div>
            <div class='res-card'>
                <div class='res-label'>Объём</div>
                <div class='res-value' id='resVol' style='color:var(--subtext);font-size:16px'>--</div>
                <div class='res-chart' id='volChart'></div>
            </div>
        </div>
        <div class='ml-card' id='mlCard' style='display:none'>
            <div class='ml-header'><span class='ml-badge'>🧠 ML</span><span class='ml-label'>Прогноз нейросети</span></div>
            <div class='ml-body'>
                <span class='ml-dir' id='mlDir'>--</span>
                <span class='ml-conf' id='mlConf'>--%</span>
            </div>
        </div>

        <div class='news-card' id='newsCard' style='display:none'>
            <div class='news-header'>
                <span class='news-badge'>📰 LLM</span>
                <span class='news-label'>Анализ новостей</span>
                <span class='news-sentiment' id='newsSentiment'>--</span>
            </div>
            <div class='news-summary' id='newsSummary'></div>
            <div class='news-headlines'>
                <span class='news-toggle' id='newsToggle' onclick='toggleNews()'>▸ Заголовки</span>
                <div class='news-list' id='newsList'></div>
            </div>
        </div>

        <div class='news-card' id='claudeCard' style='display:none'>
            <div class='news-header'>
                <span class='news-badge'>🧠 Claude Opus</span>
                <span class='news-label'>AI анализ графика</span>
                <span class='news-sentiment' id='claudeSentiment'>--</span>
            </div>
            <div class='news-summary' id='claudeReasoning'></div>
        </div>

        <div class='levels-bar' id='levelsBar'>
            <div class='level-line' id='ll1'><span class='tag l1'>L1</span><span class='info'>Индикаторы</span><span class='result' id='ll1res'></span></div>
            <div class='level-line' id='ll2'><span class='tag l2'>L2</span><span class='info'>S/R + Объём</span><span class='result' id='ll2res'></span></div>
            <div class='level-line' id='ll3'><span class='tag l3'>L3</span><span class='info'>Мульти-ТФ</span><span class='result' id='ll3res'></span></div>
            <div class='levels-divider'></div>
            <div class='levels-total'><span id='ltotalVotes'>--</span><span class='dir' id='ltotalDir'>--</span></div>
            <div class='levels-divider'></div>
            <div style='padding:6px 4px 2px;font-size:11px;font-weight:700;color:var(--subtext);letter-spacing:0.04em;text-transform:uppercase'>График цены</div>
            <canvas id='priceChart' style='display:block;width:100%;height:160px'></canvas>
            <div style='margin-top:10px;padding:8px 10px;font-size:10.5px;line-height:1.45;color:var(--dim);background:rgba(124,77,255,0.05);border:1px solid rgba(124,77,255,0.1);border-radius:10px'>
                Данные: Binance (крипта) / Yahoo Finance (форекс, акции, сырьё). Котировки могут отличаться от Pocket Option, особенно на OTC-парах. Анализ не является финансовой рекомендацией.
            </div>
        </div>

        <div class='alert-section' id='alertSection'>
            <div class='alert-toggle' onclick='toggleAlerts()'>
                <span>\uD83D\uDD14 Умные оповещения</span>
                <span class='alert-arrow' id='alertArrow'>▶</span>
            </div>
            <div class='alert-body' id='alertBody' style='display:none'>
                <div class='alert-form'>
                    <select class='alert-input' id='alertAsset'><option>BTC/USDT</option><option>ETH/USDT</option><option>SOL/USDT</option></select>
                    <select class='alert-input' id='alertIndicator'>
                        <option value='rsi'>RSI</option>
                        <option value='price'>Цена</option>
                        <option value='volume'>Объём (x3+)</option>
                    </select>
                    <select class='alert-input' id='alertCondition'>
                        <option value='below'>Ниже</option>
                        <option value='above'>Выше</option>
                    </select>
                    <input class='alert-input' id='alertThreshold' type='number' value='30' step='1'>
                    <button class='alert-btn' onclick='addAlert()'>+</button>
                </div>
                <div class='alert-list' id='alertList'></div>
            </div>
        </div>
    </div>

    <div id='screen-profile' class='app-screen' style='padding-bottom:80px'>
        <div class='hist-header'>История запросов</div>
        <div class='filter-row'>
            <div class='filter-btn active' data-days='0' onclick='setHistoryFilter(this,0)'>Все</div>
            <div class='filter-btn' data-days='1' onclick='setHistoryFilter(this,1)'>1Д</div>
            <div class='filter-btn' data-days='5' onclick='setHistoryFilter(this,5)'>5Д</div>
            <div class='filter-btn' data-days='7' onclick='setHistoryFilter(this,7)'>Нед</div>
            <div class='filter-btn' data-days='30' onclick='setHistoryFilter(this,30)'>Месяц</div>
        </div>
        <div class='history-list' id='historyList'><div class='history-empty'>Пока нет записей</div></div>
    </div>

    <div id='screen-heatmap' class='app-screen' style='padding-bottom:80px'>
        <div class='heatmap-header'>\uD83D\uDD25 Тепловая карта ликвидаций</div>
        <div class='heatmap-controls' id='heatmapControls'>
            <div class='heatmap-sym-btn active' data-hsym='BTC/USDT' onclick='setHeatmapSymbol(this)'>BTC/USDT</div>
            <div class='heatmap-sym-btn' data-hsym='ETH/USDT' onclick='setHeatmapSymbol(this)'>ETH/USDT</div>
            <div class='heatmap-sym-btn' data-hsym='SOL/USDT' onclick='setHeatmapSymbol(this)'>SOL/USDT</div>
        </div>
        <div class='heatmap-canvas-wrap'>
            <canvas id='heatmapCanvas'></canvas>
        </div>
        <div class='heatmap-legend'>
            <span><span class='bar long'></span> Лонг ликвидации (\u2191)</span>
            <span><span class='bar short'></span> Шорт ликвидации (\u2193)</span>
            <span style='margin-left:auto' id='heatmapCount'>0 событий</span>
        </div>
    </div>

    <div class='nav-bar'>
        <div class='nav-item active' data-tab='home' onclick='switchTab(this)'>
            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M3 12l9-9 9 9'/><path d='M5 10v10a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-4a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v4a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1V10'/></svg>
            <span>Главная</span>
        </div>
        <div class='nav-item' data-tab='profile' onclick='switchTab(this)'>
            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2'/><circle cx='12' cy='7' r='4'/></svg>
            <span>Профиль</span>
        </div>
        <div class='nav-item' data-tab='heatmap' onclick='switchTab(this)'>
            <svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z'/><polyline points='3.27 6.96 12 12.01 20.73 6.96'/><line x1='12' y1='22.08' x2='12' y2='12'/></svg>
            <span>Карта</span>
        </div>
    </div>

    <script>
        const tg = window.Telegram.WebApp;
        if(tg) tg.expand();

        let currentAsset = 'EUR/USD OTC';
        let currentTf = 'm1';

        const assetsData = {
            fiat: {
                otc: ['EUR/USD OTC', 'GBP/USD OTC', 'USD/JPY OTC', 'EUR/JPY OTC', 'GBP/JPY OTC', 'AUD/USD OTC', 'USD/CAD OTC', 'USD/CHF OTC', 'NZD/USD OTC', 'EUR/GBP OTC', 'AUD/CAD OTC', 'CAD/CHF OTC', 'EUR/CHF OTC', 'EUR/NZD OTC', 'NZD/JPY OTC', 'USD/BRL OTC', 'USD/IDR OTC', 'USD/PKR OTC', 'USD/DZD OTC', 'NGN/USD OTC', 'LBP/USD OTC', 'TND/USD OTC', 'JOD/CNY OTC', 'OMR/CNY OTC', 'SAR/CNY OTC']
            },
            commodities: {
                otc: ['GOLD OTC', 'SILVER OTC', 'BRENT OTC', 'OIL OTC']
            },
            crypto: {
                otc: ['BTC/USDT OTC', 'ETH/USDT OTC', 'SOL/USDT OTC']
            },
            stocks: {
                otc: ['AAPL OTC', 'TSLA OTC', 'AMZN OTC', 'GOOGL OTC', 'MSFT OTC']
            }
        };

        function getTopAssets() {
            try {
                const h = JSON.parse(localStorage.getItem('vhistory') || '[]');
                var freq = {};
                for (var i = 0; i < h.length; i++) { var e = h[i]; freq[e.asset] = (freq[e.asset] || 0) + 1; }
                return Object.keys(freq).sort(function(a,b) { return freq[b] - freq[a]; }).slice(0, 8);
            } catch(e) { return []; }
        }

        function renderAssets(arr) {
            const top = getTopAssets();
            return arr.map(function(a) {
                var star = top.indexOf(a) !== -1 ? '<span class=\x27top-star\x27>★</span>' : '';
                return '<div class=\x27asset-item\x27 data-asset=\x27' + a + '\x27 onclick=\x27setAsset(this)\x27>' + a + star + '</div>';
            }).join('');
        }

        function changeTopCategory(el) {
            document.querySelectorAll('.top-cat-btn').forEach(c => c.classList.remove('active'));
            el.classList.add('active');
            let cat = el.getAttribute('data-cat');
            document.getElementById('assetGrid').innerHTML = `<div class='otc-scroll' style='grid-column:1/-1'><div class='asset-grid'>${renderAssets(assetsData[cat].otc)}</div></div>`;
            let firstAssetEl = document.querySelector('.asset-item');
            if(firstAssetEl) setAsset(firstAssetEl);
        }

        function toggleMenu(m, b) {
            document.querySelectorAll('.asset-menu, .tf-menu').forEach(menu => { if(menu.id !== m) menu.classList.remove('show'); });
            document.getElementById(m).classList.toggle('show');
        }

        function setAsset(el) {
            let a = el.getAttribute('data-asset');
            currentAsset = a;
            document.getElementById('selectedAsset').innerText = a;
            document.querySelectorAll('.asset-item').forEach(i => i.classList.remove('active'));
            el.classList.add('active');
            document.getElementById('assetMenu').classList.remove('show');
        }

        function setTf(el) {
            let tf = el.getAttribute('data-tf');
            currentTf = tf.toLowerCase();
            document.getElementById('selectedTf').innerText = tf;
            document.querySelectorAll('.tf-btn').forEach(i => i.classList.remove('active'));
            el.classList.add('active');
            document.getElementById('tfMenu').classList.remove('show');
        }

        function switchTab(el) {
            let tab = el.getAttribute('data-tab');
            document.querySelectorAll('.nav-item').forEach(i => i.classList.remove('active'));
            el.classList.add('active');
            document.querySelectorAll('.app-screen').forEach(s => s.classList.remove('active'));
            document.getElementById('screen-' + tab).classList.add('active');
        }

        document.addEventListener('click', function(e) {
            if (!e.target.closest('.selector-section')) {
                document.querySelectorAll('.asset-menu, .tf-menu').forEach(m => m.classList.remove('show'));
            }
        });

        (function() {
            var p = new URLSearchParams(window.location.search);
            var a = p.get('asset'), t = p.get('tf');
            if (a) {
                var el = document.querySelector('.asset-item[data-asset=""' + a.toUpperCase() + '""]');
                if (el) { setAsset(el); el.scrollIntoView && el.scrollIntoView({ block: 'nearest' }); }
            }
            if (t) {
                var el = document.querySelector('.tf-btn[data-tf=""' + t.toUpperCase() + '""]');
                if (el) setTf(el);
            }
        })();

        changeTopCategory(document.querySelector('.top-cat-btn'));
        renderHistory(0);
        syncTime();

        var timeOffset = 0;

        async function syncTime() {
            try {
                var r = await fetch('/api/time');
                var d = await r.json();
                timeOffset = d.t - Date.now();
            } catch(e) { timeOffset = 0; }
        }

        function getTfSeconds() {
            const map = { s5:5, s15:15, s30:30, m1:60, m2:120, m3:180, m5:300, m15:900, m30:1800, h1:3600, h4:14400, d1:86400 };
            return map[currentTf] || 60;
        }

        function updateCountdown() {
            const tfSec = getTfSeconds();
            const now = Math.floor((Date.now() + timeOffset) / 1000);
            const remaining = tfSec - (now % tfSec);
            const mins = Math.floor(remaining / 60);
            const secs = remaining % 60;
            const el = document.getElementById('candleTime');
            if (!el) return;
            el.innerText = `${mins}:${secs.toString().padStart(2,'0')}`;
            el.className = 'time' + (remaining <= 5 ? ' critical' : remaining <= 15 ? ' warning' : '');
        }

        function clearResults() {
            document.getElementById('resProb').innerText = '--%';
            document.getElementById('resProb').style.color = 'var(--accent)';
            document.getElementById('resDir').innerText = '--';
            document.getElementById('resDir').style.color = 'var(--subtext)';
            document.getElementById('resDur').innerText = '--';
            document.getElementById('resRsi').innerText = '--';
            document.getElementById('resRsi').style.color = 'var(--subtext)';
            document.getElementById('resEma').innerText = '--';
            document.getElementById('resVol').innerText = '--';
            document.getElementById('resVol').style.color = 'var(--subtext)';
            document.getElementById('probChart').innerHTML = '';
            document.getElementById('dirChart').innerHTML = '<svg viewBox=\'0 0 80 40\'><path d=\'M10 35 L40 5 L70 35\' stroke=\'var(--dim)\' stroke-width=\'2.5\' fill=\'none\' stroke-linecap=\'round\' stroke-linejoin=\'round\' opacity=\'0.3\'/></svg>';
            document.getElementById('durChart').innerHTML = '';
            document.getElementById('levelsBar').style.display = 'none';
            document.getElementById('mlCard').style.display = 'none';
            document.getElementById('claudeCard').style.display = 'none';
            document.getElementById('newsCard').style.display = 'none';
            document.getElementById('welcomeSec').classList.remove('compact');
            document.querySelectorAll('.res-card').forEach(c => c.classList.remove('flash'));
        }

        /* ─── History ─── */
        function getHistory() {
            try { return JSON.parse(localStorage.getItem('vhistory') || '[]'); } catch(e) { return []; }
        }
        function saveToHistory(data) {
            const h = getHistory();
            h.unshift({
                ts: Date.now(),
                asset: currentAsset,
                tf: currentTf,
                direction: data.direction,
                probability: data.probability,
                duration: data.duration
            });
            if (h.length > 500) h.length = 500;
            localStorage.setItem('vhistory', JSON.stringify(h));
            renderHistory(0);
        }
        function setHistoryFilter(el, days) {
            document.querySelectorAll('.filter-btn').forEach(b => b.classList.remove('active'));
            el.classList.add('active');
            renderHistory(days);
        }
        function getFilteredHistory(days) {
            const h = getHistory();
            if (days <= 0) return h;
            const cutoff = Date.now() - days * 86400000;
            return h.filter(e => e.ts >= cutoff);
        }
        function renderHistory(days) {
            const container = document.getElementById('historyList');
            if (!container) return;
            const filtered = getFilteredHistory(days);
            if (filtered.length === 0) {
                container.innerHTML = '<div class=\x27history-empty\x27>Нет записей</div>';
                return;
            }
            container.innerHTML = filtered.slice(0, 100).map(e => {
                const d = e.direction === 'BUY' ? 'BUY' : 'PUT';
                const t = new Date(e.ts);
                const timeStr = t.toLocaleDateString('ru-RU', {day:'numeric',month:'short'}) + ' ' + t.toLocaleTimeString('ru-RU', {hour:'2-digit',minute:'2-digit'});
                return `<div class='history-item'>
                    <div><div class='hi-asset'>${e.asset}</div><div class='hi-tf'>${e.tf.toUpperCase()} · ${timeStr}</div></div>
                    <div style='text-align:right'><div class='hi-dir ${d.toLowerCase()}'>${d}</div><div class='hi-prob' style='color:${e.probability >= 90 ? 'var(--green)' : e.probability >= 85 ? '#ffd600' : 'var(--accent)'}'>${e.probability}%</div></div>
                </div>`;
            }).join('');
        }

        function flashResults() {
            document.querySelectorAll('.res-card').forEach(c => {
                c.classList.remove('flash');
                void c.offsetWidth;
                c.classList.add('flash');
            });
        }

        setInterval(updateCountdown, 1000);
        setTimeout(updateCountdown, 100);

        function renderMiniChart(containerId, values, color) {
            const container = document.getElementById(containerId);
            if(!container) return;
            const max = Math.max(...values, 1);
            container.innerHTML = values.map(v => {
                const h = Math.max(4, (v / max) * 38);
                return `<div class='res-chart-bar ${color}' style='height:${h}px'></div>`;
            }).join('');
        }

        function renderDirSvg(direction) {
            const chart = document.getElementById('dirChart');
            if(!chart) return;
            if(direction === 'BUY') {
                chart.innerHTML = `<svg viewBox='0 0 80 40'><path d='M10 35 L30 25 L45 30 L70 5' stroke='#00e676' stroke-width='3' fill='none' stroke-linecap='round' stroke-linejoin='round'/><circle cx='70' cy='5' r='3.5' fill='#00e676'/></svg>`;
            } else if(direction === 'PUT') {
                chart.innerHTML = `<svg viewBox='0 0 80 40'><path d='M10 5 L30 15 L45 10 L70 35' stroke='#ff1744' stroke-width='3' fill='none' stroke-linecap='round' stroke-linejoin='round'/><circle cx='70' cy='35' r='3.5' fill='#ff1744'/></svg>`;
            } else {
                chart.innerHTML = `<svg viewBox='0 0 80 40'><path d='M10 35 L40 5 L70 35' stroke='var(--dim)' stroke-width='2.5' fill='none' stroke-linecap='round' stroke-linejoin='round' opacity='0.3'/></svg>`;
            }
        }

        /* ─── Status bar animation (non-blocking) ─── */
        const sbStatuses = ['ЗАГРУЗКА ДАННЫХ', 'ПОЛУЧЕНИЕ ЦЕНЫ', 'АНАЛИЗ РЫНКА'];
        let sbTimer = null, sbIdx = 0;

        function startStatusBar() {
            const sb = document.getElementById('statusBar');
            if (!sb) return;
            sb.classList.add('show');
            const title = document.getElementById('sbTitle');
            const sub = document.getElementById('sbSub');
            if (title) title.innerHTML = 'АНАЛИЗИРУЮ РЫНОК<span class=\'blink\'>.</span>';
            if (sub) { sub.textContent = sbStatuses[0]; sub.className = 'sb-sub'; }
            sbIdx = 0;

            if (sbTimer) clearInterval(sbTimer);
            sbTimer = setInterval(() => {
                const title = document.getElementById('sbTitle');
                if (title) {
                    const m = title.textContent.match(/\.+$/);
                    const dots = m ? m[0].length : 0;
                    title.innerHTML = 'АНАЛИЗИРУЮ РЫНОК<span class=\'blink\'>' + '.'.repeat((dots % 3) + 1) + '</span>';
                }
                sbIdx = (sbIdx + 1) % sbStatuses.length;
                const sub = document.getElementById('sbSub');
                if (sub) {
                    sub.classList.add('fade');
                    setTimeout(() => { sub.textContent = sbStatuses[sbIdx]; sub.classList.remove('fade'); }, 200);
                }
            }, 900);
        }

        function stopStatusBar() {
            const sb = document.getElementById('statusBar');
            if (sb) sb.classList.remove('show');
            if (sbTimer) { clearInterval(sbTimer); sbTimer = null; }
        }

        function pricesToBars(prices, count) {
            if (!prices || !prices.length) return [];
            const tail = prices.slice(-count);
            const min = Math.min.apply(null, tail);
            const max = Math.max.apply(null, tail);
            const span = max - min;
            if (span < 1e-12) return tail.map(() => 0.5);
            return tail.map(p => 0.05 + 0.9 * (p - min) / span);
        }

        function renderPriceChart(canvasId, prices, direction) {
            const c = document.getElementById(canvasId);
            if (!c || !prices || prices.length < 2) return;
            const dpr = window.devicePixelRatio || 1;
            const cssW = c.clientWidth || c.parentNode.clientWidth || 320;
            const cssH = c.clientHeight || 140;
            c.width = Math.round(cssW * dpr);
            c.height = Math.round(cssH * dpr);
            const ctx = c.getContext('2d');
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, cssW, cssH);

            const pad = { l: 8, r: 8, t: 10, b: 10 };
            const w = cssW - pad.l - pad.r;
            const h = cssH - pad.t - pad.b;
            const min = Math.min.apply(null, prices);
            const max = Math.max.apply(null, prices);
            const span = Math.max(max - min, 1e-12);
            const x = i => pad.l + (i / (prices.length - 1)) * w;
            const y = v => pad.t + h - ((v - min) / span) * h;

            ctx.strokeStyle = 'rgba(124,77,255,0.08)';
            ctx.lineWidth = 1;
            for (let k = 1; k < 4; k++) {
                const gy = pad.t + (h / 4) * k;
                ctx.beginPath(); ctx.moveTo(pad.l, gy); ctx.lineTo(pad.l + w, gy); ctx.stroke();
            }

            const stroke = direction === 'BUY' ? '#00e676'
                          : direction === 'PUT' ? '#ff1744'
                          : '#7c4dff';
            const fillTop = direction === 'BUY' ? 'rgba(0,230,118,0.35)'
                          : direction === 'PUT' ? 'rgba(255,23,68,0.35)'
                          : 'rgba(124,77,255,0.35)';

            const grad = ctx.createLinearGradient(0, pad.t, 0, pad.t + h);
            grad.addColorStop(0, fillTop);
            grad.addColorStop(1, 'rgba(0,0,0,0)');
            ctx.beginPath();
            ctx.moveTo(x(0), pad.t + h);
            for (let i = 0; i < prices.length; i++) ctx.lineTo(x(i), y(prices[i]));
            ctx.lineTo(x(prices.length - 1), pad.t + h);
            ctx.closePath();
            ctx.fillStyle = grad;
            ctx.fill();

            ctx.beginPath();
            ctx.moveTo(x(0), y(prices[0]));
            for (let i = 1; i < prices.length; i++) ctx.lineTo(x(i), y(prices[i]));
            ctx.strokeStyle = stroke;
            ctx.lineWidth = 2;
            ctx.shadowColor = stroke;
            ctx.shadowBlur = 8;
            ctx.stroke();
            ctx.shadowBlur = 0;

            const lx = x(prices.length - 1), ly = y(prices[prices.length - 1]);
            ctx.beginPath();
            ctx.arc(lx, ly, 3.5, 0, Math.PI * 2);
            ctx.fillStyle = stroke;
            ctx.fill();

            const lastTxt = prices[prices.length - 1].toFixed(prices[0] > 100 ? 2 : 5);
            ctx.font = '600 11px Inter, sans-serif';
            ctx.fillStyle = stroke;
            const tw = ctx.measureText(lastTxt).width;
            ctx.fillRect(lx - tw - 10, ly - 9, tw + 8, 16);
            ctx.fillStyle = '#0b0a1f';
            ctx.fillText(lastTxt, lx - tw - 6, ly + 3);
        }

        document.getElementById('btnGet').onclick = async () => {
            const btn = document.getElementById('btnGet');
            const sphere = document.getElementById('mainSphere');

            clearResults();
            startStatusBar();

            requestAnimationFrame(() => {
                sphere.classList.add('analyzing');
                btn.disabled = true;
                btn.innerText = 'СКАНИРОВАНИЕ...';
            });

            try {
                const res = await fetch(`/api/analyze?asset=${encodeURIComponent(currentAsset)}&timeframe=${currentTf}`);
                const data = await res.json();

                stopStatusBar();

                setTimeout(() => {
                    sphere.classList.remove('analyzing');
                    btn.disabled = false;
                    btn.innerText = 'ПОЛУЧИТЬ АНАЛИЗ';

                    if(data.error) {
                        if(tg && tg.showAlert) tg.showAlert(data.error);
                        else alert(data.error);
                        return;
                    }

                    const isUnclear = data.unclear === true;
                    document.getElementById('resDir').innerText = data.direction === 'BUY' ? 'ВВЕРХ' : 'ВНИЗ';
                    document.getElementById('resDir').style.color = data.direction === 'BUY' ? '#00e676' : '#ff1744';

                    document.getElementById('resProb').innerText = data.probability + '%';
                    document.getElementById('resProb').style.color = data.probability >= 90 ? '#00e676' : data.probability >= 85 ? '#ffd600' : 'var(--accent)';

                    document.getElementById('resDur').innerText = data.duration;

                    if (data.rsi !== undefined) {
                        const rsiEl = document.getElementById('resRsi');
                        rsiEl.innerText = data.rsi;
                        rsiEl.style.color = data.rsi > 70 ? '#ff1744' : data.rsi < 30 ? '#00e676' : 'var(--subtext)';
                    }
                    if (data.ema !== undefined) {
                        document.getElementById('resEma').innerText = data.ema;
                    }
                    if (data.volumeStrength !== undefined) {
                        const volEl = document.getElementById('resVol');
                        const vs = data.volumeStrength;
                        volEl.innerText = vs > 0 ? '\u2B06 ' + vs.toFixed(1) : vs < 0 ? '\u2B07 ' + Math.abs(vs).toFixed(1) : '\u2014';
                        volEl.style.color = vs > 0.5 ? '#00e676' : vs < -0.5 ? '#ff1744' : 'var(--subtext)';
                    }
                    if (data.tfConflict) {
                        document.getElementById('resProb').innerText += ' \u26A0\uFE0F';
                    }
                    if (data.mlDirection && data.mlDirection !== 'NEUTRAL') {
                        const mc = document.getElementById('mlCard');
                        mc.style.display = 'flex';
                        const dirEl = document.getElementById('mlDir');
                        dirEl.innerText = data.mlDirection === 'BUY' ? '\u2191 ВВЕРХ' : '\u2193 ВНИЗ';
                        dirEl.style.color = data.mlDirection === 'BUY' ? '#00e676' : '#ff1744';
                        document.getElementById('mlConf').innerText = data.mlConfidence + '%';
                    }

                    if (data.newsScore !== undefined) {
                        const nc = document.getElementById('newsCard');
                        nc.style.display = 'block';
                        const senEl = document.getElementById('newsSentiment');
                        senEl.innerText = data.newsSentiment;
                        if (data.newsScore > 0.5) { senEl.style.color = '#00e676'; }
                        else if (data.newsScore < -0.5) { senEl.style.color = '#ff1744'; }
                        else { senEl.style.color = 'var(--subtext)'; }
                        document.getElementById('newsSummary').innerText = data.newsSummary;
                        const nl = document.getElementById('newsList');
                        if (data.newsHeadlines && data.newsHeadlines.length) {
                            nl.innerHTML = data.newsHeadlines.map(h => `<div class='news-list-item'>${h}</div>`).join('');
                        } else {
                            nl.innerHTML = '';
                        }
                    }

                    if (data.claudeDirection && data.claudeReasoning) {
                        const cc = document.getElementById('claudeCard');
                        cc.style.display = 'block';
                        const senEl = document.getElementById('claudeSentiment');
                        senEl.innerText = data.claudeDirection === 'BUY' ? 'ВВЕРХ' : data.claudeDirection === 'PUT' ? 'ВНИЗ' : '—';
                        senEl.style.color = data.claudeDirection === 'BUY' ? '#a78bfa' : data.claudeDirection === 'PUT' ? '#f472b6' : 'var(--subtext)';
                        let reasoningText = data.claudeReasoning;
                        if (data.claudeProbability) {
                            reasoningText += ` (вероятность: ${data.claudeProbability}%)`;
                        }
                        document.getElementById('claudeReasoning').innerText = reasoningText;
                    }

                    const probBars = pricesToBars(data.chartData, 16);
                    if (probBars.length) renderMiniChart('probChart', probBars, '');

                    renderDirSvg(data.direction);

                    const durBars = pricesToBars(data.chartData, 8);
                    if (durBars.length) renderMiniChart('durChart', durBars, '');

                    renderPriceChart('priceChart', data.chartData || [], data.direction);

                    if(data.levels) {
                        const L = data.levels;
                        const renderLevel = (id, lv) => {
                            document.getElementById(id).className = `result ${lv.direction.toLowerCase()}`;
                            document.getElementById(id).innerText = `${lv.direction === 'NEUTRAL' ? '\u2014' : lv.direction}`;
                            document.getElementById(id.replace('res', '')).style.display = 'flex';
                        };
                        renderLevel('ll1res', L.level1);
                        renderLevel('ll2res', L.level2);
                        renderLevel('ll3res', L.level3);
                        document.getElementById('ltotalVotes').innerHTML = `<span style='color:var(--green)'>\u2191 ${data.levels.level1.buy + data.levels.level2.buy + data.levels.level3.buy}</span> / <span style='color:var(--red)'>\u2193 ${data.levels.level1.put + data.levels.level2.put + data.levels.level3.put}</span>`;
                        const td = document.getElementById('ltotalDir');
                        td.className = `dir ${data.direction.toLowerCase()}`;
                        td.innerText = data.direction === 'BUY' ? '\u2191 ВВЕРХ' : '\u2193 ВНИЗ';
                        document.getElementById('levelsBar').style.display = 'block';
                    }

                    saveToHistory(data);
                    flashResults();

                }, 2000);
            } catch(e) {
                stopStatusBar();
                sphere.classList.remove('analyzing');
                btn.disabled = false;
                btn.innerText = 'ПОЛУЧИТЬ АНАЛИЗ';
            }
        };

        /* ─── Alerts ─── */
        let tgChatId = 0;

        function toggleAlerts() {
            const body = document.getElementById('alertBody');
            const arrow = document.getElementById('alertArrow');
            const show = body.style.display !== 'block';
            body.style.display = show ? 'block' : 'none';
            arrow.classList.toggle('open', show);
            if (show) loadAlerts();
        }

        async function loadAlerts() {
            try {
                const res = await fetch('/api/alerts');
                const data = await res.json();
                const list = document.getElementById('alertList');
                if (!data.length) { list.innerHTML = '<div style=\'color:var(--dim);font-size:11px\'>Нет оповещений</div>'; return; }
                list.innerHTML = data.map(a => '<div class=\'alert-item\'><span>' + a.label + '</span><span class=\'del\' onclick=\'deleteAlert(\""' + a.id + '\"")\'>\u2716</span></div>').join('');
            } catch {}
        }

        async function addAlert() {
            const asset = document.getElementById('alertAsset').value;
            const ind = document.getElementById('alertIndicator').value;
            const cond = document.getElementById('alertCondition').value;
            const thresh = parseFloat(document.getElementById('alertThreshold').value);
            await fetch('/api/alerts', {
                method: 'POST',
                headers: {'Content-Type':'application/json'},
                body: JSON.stringify({ asset, indicator: ind, condition: cond, threshold: thresh })
            });
            loadAlerts();
        }

        async function deleteAlert(id) {
            await fetch('/api/alerts/' + id, { method: 'DELETE' });
            loadAlerts();
        }

        // Set chat ID from Telegram init
        try {
            if (window.Telegram && window.Telegram.WebApp) {
                tgChatId = window.Telegram.WebApp.initDataUnsafe?.user?.id || 0;
                if (tgChatId) fetch('/api/alerts/chatid', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({chatId: tgChatId}) });
            }
        } catch {}

        /* ─── News toggle ─── */
        function toggleNews() {
            const list = document.getElementById('newsList');
            const toggle = document.getElementById('newsToggle');
            const open = list.classList.toggle('open');
            toggle.innerText = open ? '\u25BD Заголовки' : '\u25B8 Заголовки';
        }

        /* ─── Liquidation Heatmap ─── */
        let currentHeatmapSymbol = 'BTC/USDT';
        let heatmapTimer = null;

        function setHeatmapSymbol(el) {
            document.querySelectorAll('.heatmap-sym-btn').forEach(b => b.classList.remove('active'));
            el.classList.add('active');
            currentHeatmapSymbol = el.getAttribute('data-hsym');
            fetchAndRenderHeatmap();
        }

        async function fetchAndRenderHeatmap() {
            try {
                const res = await fetch('/api/liquidations');
                const data = await res.json();
                const levels = data[currentHeatmapSymbol] || [];
                document.getElementById('heatmapCount').innerText = levels.length + ' уровней';
                renderHeatmapCanvas('heatmapCanvas', levels);
            } catch { return; }
        }

        function renderHeatmapCanvas(canvasId, levels) {
            const c = document.getElementById(canvasId);
            if (!c) return;
            if (!levels || levels.length === 0) {
                c.style.display = 'none';
                document.querySelector('.heatmap-canvas-wrap').innerHTML = '<div class=\'heatmap-empty\'>Нет данных о ликвидациях. Ожидание событий...</div>';
                return;
            }
            c.style.display = 'block';

            const dpr = window.devicePixelRatio || 1;
            const cssW = c.parentNode.clientWidth || 320;
            const maxBars = 30;
            const barH = 18;
            const pad = { l: 60, r: 10, t: 8, b: 8 };
            const cssH = Math.max(120, Math.min(levels.length, maxBars) * barH + pad.t + pad.b);
            c.width = Math.round(cssW * dpr);
            c.height = Math.round(cssH * dpr);
            c.style.width = cssW + 'px';
            c.style.height = cssH + 'px';
            const ctx = c.getContext('2d');
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, cssW, cssH);

            const display = levels.slice(0, maxBars);
            const maxVol = Math.max(1, ...display.map(l => Math.max(l.longVol || 0, l.shortVol || 0)));
            const w = cssW - pad.l - pad.r;
            const chartMid = pad.l + w / 2;

            display.forEach((l, i) => {
                const y = pad.t + i * barH;
                const longW = ((l.longVol || 0) / maxVol) * (w / 2);
                const shortW = ((l.shortVol || 0) / maxVol) * (w / 2);

                // Short liquidations (red, go left)
                if (shortW > 0) {
                    const g = ctx.createLinearGradient(chartMid - shortW, 0, chartMid, 0);
                    g.addColorStop(0, 'rgba(255,23,68,0.9)');
                    g.addColorStop(1, 'rgba(255,23,68,0.1)');
                    ctx.fillStyle = g;
                    ctx.beginPath();
                    ctx.roundRect(chartMid - shortW, y + 1, shortW, barH - 4, [0, 3, 3, 0]);
                    ctx.fill();
                }

                // Long liquidations (green, go right)
                if (longW > 0) {
                    const g = ctx.createLinearGradient(chartMid, 0, chartMid + longW, 0);
                    g.addColorStop(0, 'rgba(0,230,118,0.1)');
                    g.addColorStop(1, 'rgba(0,230,118,0.9)');
                    ctx.fillStyle = g;
                    ctx.beginPath();
                    ctx.roundRect(chartMid, y + 1, longW, barH - 4, [3, 0, 0, 3]);
                    ctx.fill();
                }

                // Price label
                ctx.fillStyle = '#a89fd4';
                ctx.font = '600 10px Inter, sans-serif';
                ctx.textAlign = 'right';
                ctx.fillText(l.price.toFixed(l.price > 1000 ? 0 : 2), pad.l - 6, y + barH / 2 + 3);
            });

            // Center line
            ctx.strokeStyle = 'rgba(124,77,255,0.15)';
            ctx.lineWidth = 1;
            ctx.setLineDash([3, 3]);
            ctx.beginPath();
            ctx.moveTo(chartMid, pad.t);
            ctx.lineTo(chartMid, cssH - pad.b);
            ctx.stroke();
            ctx.setLineDash([]);
        }

        // Fetch heatmap on tab switch
        const origSwitchTab = switchTab;
        switchTab = function(el) {
            origSwitchTab(el);
            const tab = el.getAttribute('data-tab');
            if (tab === 'heatmap') {
                if (heatmapTimer) clearInterval(heatmapTimer);
                fetchAndRenderHeatmap();
                heatmapTimer = setInterval(fetchAndRenderHeatmap, 15000);
            } else {
                if (heatmapTimer) { clearInterval(heatmapTimer); heatmapTimer = null; }
            }
        };

        /* ─── Fear & Greed ─── */
        async function fetchFearGreed() {
            try {
                const res = await fetch('/api/fear-greed');
                const data = await res.json();
                const el = document.getElementById('mbFng');
                if (!el) return;
                const v = data.value;
                const emoji = v <= 25 ? '\uD83D\uDE28' : v <= 40 ? '\uD83D\uDE41' : v <= 55 ? '\uD83D\uDE10' : v <= 70 ? '\uD83D\uDE0A' : '\uD83D\uDE0E';
                el.innerText = `${emoji} ${v} · ${data.classification}`;
                el.style.color = v <= 25 ? '#ff1744' : v <= 40 ? '#ff9100' : v <= 55 ? '#ffd600' : v <= 70 ? '#aeea00' : '#00e676';
            } catch {}
        }

        /* ─── Market ticker ─── */
        async function fetchMarketStatus() {
            try {
                const res = await fetch('/api/market-status');
                const data = await res.json();
                const prices = data.prices || {};
                for (const [sym, info] of Object.entries(prices)) {
                    const id = 'mb' + sym.split('/')[0];
                    const el = document.getElementById(id);
                    if (!el) continue;
                    const p = info.price;
                    el.innerText = p > 100 ? p.toFixed(0) : p > 1 ? p.toFixed(2) : p.toFixed(4);
                }
            } catch {}
        }

        fetchFearGreed();
        fetchMarketStatus();
        setInterval(fetchFearGreed, 60000);
        setInterval(fetchMarketStatus, 5000);
    </script>
</body>
</html>";
    }
}
