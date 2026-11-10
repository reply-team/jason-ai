import { describe, expect, it } from 'vitest';
import { act, define, parseArgs } from './actions';
import { markupOf } from './html';

describe('the console actions', () => {
    it('travel as a name and a JSON literal, escaped for the attribute', () => {
        expect(markupOf(act('needs.open'))).toBe('data-action="needs.open"');
        expect(markupOf(act('needs.answer', 'dec_1', '"); alert(1); ("'))).toBe(
            'data-action="needs.answer" data-args="[&quot;dec_1&quot;,&quot;\\&quot;); alert(1); (\\&quot;&quot;]"',
        );
    });

    it('read their arguments as JSON and never as code', () => {
        expect(parseArgs('["dec_1",2,true,null]')).toEqual(['dec_1', 2, true, null]);
        expect(parseArgs(undefined)).toEqual([]);
        expect(parseArgs('alert(1)')).toEqual([]);
        expect(parseArgs('{"a":1}')).toEqual([]);
    });

    it('are each defined once', () => {
        define('test.once', () => undefined);
        expect(() => define('test.once', () => undefined)).toThrow(/defined twice/);
    });
});
