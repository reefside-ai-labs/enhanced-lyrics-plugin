import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installPlaybackBridge } from '../bridge.mjs';
function manager() { return { getCurrentPlayer() {}, getPlayerState() {}, currentTime() {}, seek() {}, paused() {}, playPause() {} }; }
function factory(module) { const playback = manager(); playback.getCurrentPlayer(); playback.playPause(); module.exports.A = playback; }
test('captures a minified export when its module naturally executes', () => {
    const scope = {}; let captured;
    installPlaybackBridge(scope, value => { captured = value; });
    const chunk = [[1], { 42: factory }];
    scope.webpackChunk.push(chunk);
    const module = { exports: {} };
    chunk[1][42](module);
    assert.equal(captured, module.exports.A);
});
test('webpack replacement of push prepares modules before registration without recursion', () => {
    const scope = {}; let captured; let calls = 0;
    installPlaybackBridge(scope, value => { captured = value; calls++; });
    const previous = scope.webpackChunk.push.bind(scope.webpackChunk);
    scope.webpackChunk.push = chunk => {
        for (const fn of Object.values(chunk[1])) fn({ exports: {} });
        previous(chunk);
    };
    scope.webpackChunk.push([[1], { 42: factory }]);
    assert.ok(captured);
    assert.equal(calls, 1);
    assert.equal(scope.webpackChunk.length, 1);
});
test('does not execute unrelated modules or change their identity', () => {
    const scope = {}; let ran = false;
    const unrelated = () => { ran = true; };
    installPlaybackBridge(scope, () => assert.fail());
    const chunk = [[1], { 2: unrelated }];
    scope.webpackChunk.push(chunk);
    assert.equal(ran, false);
    assert.equal(chunk[1][2], unrelated);
});
