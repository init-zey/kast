/**
 * Player Model Kit — Blockbench 4.9+
 *
 * Opens the "Player" character project that ships beside this plugin — an
 * armless, hooded, hunched figure in a grey robe whose face is hidden in
 * shadow — and renders reference previews of the walk cycle from Blockbench's
 * own viewport into a previews/ folder.
 *
 * Install:  copy this file into
 *   Windows  %APPDATA%\Blockbench\plugins\
 *   macOS    ~/Library/Application Support/Blockbench/plugins/
 *   Linux    ~/.config/Blockbench/plugins/
 * then restart Blockbench.  Tools ▸ Player Kit ▸ …
 *
 * The plugin reads Player.bbmodel from its own folder; it never writes to the
 * project. Previews are written to <model folder>/../previews.
 */
(function () {
  const path = require('path');
  const fs = require('fs');

  const MODEL_FOLDER = 'D:\\document\\kast\\Assets\\player';
  const PREVIEW_FOLDER = 'D:\\document\\kast\\Assets\\previews';

  function findModel() {
    const own = Plugins.path ? path.join(Plugins.path, 'Player.bbmodel') : null;
    const candidates = [
      own,
      path.join(MODEL_FOLDER, 'Player.bbmodel'),
    ].filter(Boolean);
    for (const candidate of candidates) {
      try { if (fs.existsSync(candidate)) return candidate; } catch (err) {}
    }
    return null;
  }

  // ------------------------------------------------------------- open model
  const open_action = new Action('player_kit_open', {
    name: '打开 Player 模型',
    description: 'Open the bundled Player character project',
    icon: 'accessibility_new',
    click() {
      const file = findModel();
      if (!file) {
        Blockbench.showMessageBox({
          title: 'Player Kit',
          message: 'Player.bbmodel was not found.\n\nPut Player.bbmodel next to this plugin, or fix MODEL_FOLDER in the plugin source.',
          buttons: ['dialog.ok'],
        });
        return;
      }
      Blockbench.read([file], {}, files => {
        loadModelFile(files[0]);
        Blockbench.showQuickMessage('Player model loaded');
      });
    },
  });

  // ---------------------------------------------------------- preview sheet
  const preview_action = new Action('player_kit_previews', {
    name: '导出预览图 (各视角 / 行走帧)',
    description: 'Render front, three-quarter, side and back views of every quarter of the animation',
    icon: 'photo_camera',
    condition: () => !!Project,
    click() {
      const preview = Preview.selected;
      if (!preview || !preview.canvas) {
        Blockbench.showQuickMessage('No 3D preview available');
        return;
      }
      if (!fs.existsSync(PREVIEW_FOLDER)) fs.mkdirSync(PREVIEW_FOLDER, { recursive: true });

      const wasOrtho = preview.isOrtho;
      const camera_before = preview.camera.position.clone();
      const zoom_before = preview.camera.zoom;
      const target_before = preview.controls.target.clone();
      if (!wasOrtho) preview.setProjectionMode(true);
      preview.resize();

      const target = new THREE.Vector3(0, 15, 0);
      const views = [
        { name: 'front', yaw: 0, pitch: 0 },
        { name: 'three-quarter', yaw: 35, pitch: 8 },
        { name: 'side', yaw: 90, pitch: 0 },
        { name: 'back', yaw: 180, pitch: 0 },
      ];

      // sample the animation at quarter points (falls back to the rest pose)
      const animation = Animation.all[0];
      const frames = animation
        ? [0, 0.25, 0.5, 0.75].map(f => ({ label: 'f' + f, time: animation.length * f }))
        : [{ label: 'pose', time: null }];

      let written = 0;
      for (const frame of frames) {
        if (frame.time !== null) {
          if (typeof Timeline !== 'undefined' && Timeline.setTime) Timeline.setTime(frame.time);
          animation.select();
          if (typeof Animator !== 'undefined') Animator.preview();
        }
        for (const view of views) {
          const dir = new THREE.Vector3(
            Math.sin(view.yaw * Math.PI / 180),
            Math.sin(view.pitch * Math.PI / 180),
            -Math.cos(view.yaw * Math.PI / 180),
          ).normalize().multiplyScalar(64);
          preview.controls.target.copy(target);
          preview.camera.position.copy(target.clone().add(dir));
          preview.camera.zoom = 14;
          preview.camera.updateProjectionMatrix();
          preview.camera.lookAt(target);
          preview.controls.update();
          preview.render();
          const dataUrl = preview.canvas.toDataURL('image/png');
          const out = path.join(PREVIEW_FOLDER, `bb-${frame.label}-${view.name}.png`);
          fs.writeFileSync(out, Buffer.from(dataUrl.split(',')[1], 'base64'));
          written++;
        }
      }

      preview.camera.position.copy(camera_before);
      preview.camera.zoom = zoom_before;
      preview.controls.target.copy(target_before);
      preview.camera.updateProjectionMatrix();
      preview.controls.update();
      preview.render();
      if (!wasOrtho) preview.setProjectionMode(false);

      Blockbench.showQuickMessage(`已导出 ${written} 张预览图到 previews/`, 2500);
    },
  });

  // ---------------------------------------------------------------- reports
  const stats_action = new Action('player_kit_stats', {
    name: '模型统计',
    description: 'Bones, cubes, texture, bounds and animation summary',
    icon: 'straighten',
    condition: () => !!Project,
    click() {
      const min = [Infinity, Infinity, Infinity];
      const max = [-Infinity, -Infinity, -Infinity];
      Outliner.elements.forEach(el => {
        if (!el.from || !el.to) return;
        for (let i = 0; i < 3; i++) {
          min[i] = Math.min(min[i], el.from[i], el.to[i]);
          max[i] = Math.max(max[i], el.from[i], el.to[i]);
        }
      });
      const anims = Animation.all.map(a => {
        let keys = 0;
        for (const id in a.animators) keys += (a.animators[id].keyframes || []).length;
        return `${a.name} — loop:${a.loop} · ${a.length}s · ${keys} keyframes`;
      });
      const tex = Texture.all[0];
      new Dialog({
        id: 'player_kit_stats',
        title: 'Player 模型统计',
        width: 480,
        form: {
          info: {
            type: 'info',
            text: [
              `骨骼 Bones: ${Group.all.length}`,
              `立方体 Cubes: ${Outliner.elements.length}`,
              `贴图 Texture: ${tex ? `${tex.uv_width}x${tex.uv_height} — ${tex.name}` : '无'}`,
              `包围盒 Bounds: ${min.map(v => v.toFixed(1)).join(', ')} → ${max.map(v => v.toFixed(1)).join(', ')}`,
              `动画 Animations: ${anims.length ? anims.join(' / ') : '无'}`,
            ].join('<br>'),
          },
        },
        onConfirm() { this.hide(); },
      }).show();
    },
  });

  // ------------------------------------------------------------- register
  BBPlugin.register('player_model_kit', {
    title: 'Player Kit',
    author: 'kast',
    description: '打开随附的 Player 人物模型（灰袍、无臂、驼背、遮面），并导出各视角与行走帧预览图。',
    icon: 'accessibility_new',
    version: '1.0.0',
    variant: 'both',
    min_version: '4.9.0',
    onload() {
      MenuBar.addAction(open_action, 'tools');
      MenuBar.addAction(preview_action, 'tools');
      MenuBar.addAction(stats_action, 'tools');
    },
    onunload() {
      open_action.delete();
      preview_action.delete();
      stats_action.delete();
    },
  });
})();
