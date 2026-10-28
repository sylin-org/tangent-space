export const tangentAccents = [{name:'Amber',value:'#c8931f'},{name:'Coral',value:'#c2542f'},{name:'Jade',value:'#3b8065'},{name:'Sky',value:'#3d7ca8'},{name:'Iris',value:'#7864a8'},{name:'Rose',value:'#b45e7c'}]
export function tangentAccent(value:string) { return tangentAccents.find(tone=>tone.value===value)?.value || tangentAccents[0]!.value }
