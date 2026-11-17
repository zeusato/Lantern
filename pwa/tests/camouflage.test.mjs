import test from 'node:test';
import assert from 'node:assert/strict';

test('calculator conceals notices and chat, pauses media, clears badges and covers background returns', async()=>{
  class Element extends EventTarget {
    constructor(){super();this.dataset={};this.textContent='';this.classes=new Set();this.classList={add:x=>this.classes.add(x),remove:x=>this.classes.delete(x)};}
    close(){this.open=false;}
    setAttribute(name,value){this[name]=value;}
  }
  const ids=['#calculator','#calculator-display','#calculator-expression','#app','#toast','#sheet','meta[name="theme-color"]','#calculator-history','#calculator-screen','.calculator-keys','#calculator-history-button','#calculator-history-empty','#calculator-history-list','#calculator-basic-button'];
  const elements=Object.fromEntries(ids.map(id=>[id,new Element()]));
  let paused=0,badges=0,blurred=0;
  globalThis.document=Object.assign(new EventTarget(),{documentElement:new Element(),hidden:false,activeElement:{blur(){blurred++;}},querySelector:id=>elements[id],querySelectorAll:()=>[{pause(){paused++;}}]});
  globalThis.window=new EventTarget();
  Object.defineProperty(globalThis,'navigator',{value:{clearAppBadge:async()=>{badges++;}},configurable:true});
  const {isCalculator,showCalculator,showNotice}=await import('../camouflage.js');
  const reveal=()=>{for(const key of '123456789*91='){const event=new Event('keydown');Object.defineProperty(event,'key',{value:key});document.dispatchEvent(event);}};
  const assertCovered=()=>{assert.equal(isCalculator(),true);assert.equal(document.title,'Máy tính');assert.equal(elements['#app'].hidden,true);assert.equal(elements['#app'].inert,true);assert.equal(elements['#toast'].classes.has('visible'),false);assert.equal(elements['#toast'].textContent,'');};
  assertCovered();showNotice('Tin bí mật vừa đến');assertCovered();
  reveal();assert.equal(isCalculator(),false);assert.equal(elements['#app'].hidden,false);assert.equal(elements['#app'].inert,false);
  assert.equal(elements['#calculator-history-list'].innerHTML,''); // Never record the access expression.
  showNotice('Tin mới khi đang chat');assert.equal(elements['#toast'].classes.has('visible'),true);
  elements['#sheet'].open=true;showCalculator();assertCovered();assert.equal(elements['#sheet'].open,false);assert.ok(paused>=2&&badges>=2&&blurred>=2);
  showNotice('Thông báo cập nhật đến muộn');reveal();assert.equal(elements['#toast'].textContent,'');
  document.hidden=true;document.dispatchEvent(new Event('visibilitychange'));assertCovered();
  document.hidden=false;document.dispatchEvent(new Event('visibilitychange'));assertCovered();
  reveal();window.dispatchEvent(new Event('pagehide'));assertCovered();
  reveal();const restored=new Event('pageshow');Object.defineProperty(restored,'persisted',{value:true});window.dispatchEvent(restored);assertCovered();
});
